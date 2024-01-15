using AIAssistantAPI.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using OpenAI;
using OpenAI.Assistants;
using OpenAI.Threads;
using System.Threading;

namespace AIAssistantAPI.Services
{
    public class ChatService : IChatService
    {
        private readonly OpenAIClient _apiClient;
        private readonly IDistributedCache _cache;
        private readonly ILogger<ChatService> _logger;
        private readonly string _assistantId;

        public ChatService(IConfiguration configuration, IDistributedCache cache, ILogger<ChatService> logger)
        {
            var apiKey = configuration["OpenAI-ApiKey"];
            _assistantId = configuration["OpenAI-AssistantId"];
            _apiClient = new OpenAIClient(apiKey);
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("ChatService created with AssistantId: {AssistantId}", _assistantId);
        }

        public async Task<string> ProcessChatMessageAsync(string sessionId, string input, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"Attempting to get or create thread for SessionId: {sessionId}");
                // Retrieve existing thread ID or start a new one
                var assistant = await _apiClient.AssistantsEndpoint.RetrieveAssistantAsync(_assistantId, cancellationToken);
                var messages = new List<Message> { new(input) };
                var threadRequest = new CreateThreadRequest(messages);

                var run = await assistant.CreateThreadAndRunAsync(threadRequest);

                var response = await _apiClient.ThreadsEndpoint.RetrieveRunAsync(run.ThreadId, run.Id);

                while (response.Status != RunStatus.Completed)
                {
                    await Task.Delay(100);
                    response = await _apiClient.ThreadsEndpoint.RetrieveRunAsync(run.ThreadId, run.Id);
                }

                var messagesResponses = await response.ListMessagesAsync();
                var latestMessage = messagesResponses.Items.First();
                var responseText = latestMessage?.Content[0].Text.Value ?? "No response received.";
                await _apiClient.ThreadsEndpoint.DeleteThreadAsync(run.ThreadId);

                _logger.LogInformation($"Successfully processed chat message for SessionId: {sessionId} --- Response Text: {responseText}");
                return responseText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing chat message for sessionId: {sessionId}");
                throw;
            }
        }

        public async Task<string> ProcessChatMessageAsync_can_be_used_in_future(string sessionId, string input, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"Attempting to get or create thread for SessionId: {sessionId}");
                // Retrieve existing thread ID or start a new one
                var threadId = await GetOrCreateThreadAsync(sessionId, cancellationToken);

                // Wait for any active run to complete
                var thread = await _apiClient.ThreadsEndpoint.RetrieveThreadAsync(threadId, cancellationToken);
                var runList = await thread.ListRunsAsync();

                foreach (var run in runList.Items)
                {
                    await WaitForRunToComplete(run, cancellationToken);
                }

                // Send the message to the OpenAI thread
                var messageRequest = new CreateMessageRequest(input);
                await _apiClient.ThreadsEndpoint.CreateMessageAsync(threadId, messageRequest, cancellationToken);

                // Create and run a new thread run
                var assistant = await _apiClient.AssistantsEndpoint.RetrieveAssistantAsync(_assistantId, cancellationToken);
                var runResponse = await thread.CreateRunAsync(assistant, cancellationToken);

                // Wait for the response
                await WaitForRunToComplete(runResponse, cancellationToken);

                // Retrieve and return the latest message
                var messages = await _apiClient.ThreadsEndpoint.ListMessagesAsync(threadId, null, cancellationToken);
                var latestMessage = messages.Items.First(); // Assuming the latest message is the response
                var responseText = latestMessage?.Content[0].Text.Value ?? "No response received.";

                _logger.LogInformation($"Successfully processed chat message for SessionId: {sessionId} --- Response Text: {responseText}");
                return responseText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing chat message for sessionId: {sessionId}");
                throw;
            }
        }

        private async Task<string> GetOrCreateThreadAsync(string sessionId, CancellationToken cancellationToken)
        {
            var threadId = await _cache.GetStringAsync(sessionId, cancellationToken);

            if (string.IsNullOrEmpty(threadId))
            {
                _logger.LogInformation($"Creating new thread for AssistantId: {_assistantId}");
                // Create a new thread if not existing
                var assistant = await _apiClient.AssistantsEndpoint.RetrieveAssistantAsync(_assistantId, cancellationToken);
                var threadRequest = new CreateThreadRequest(new List<Message>());
                var thread = await assistant.CreateThreadAndRunAsync(threadRequest, cancellationToken);
                threadId = thread.ThreadId;


                _logger.LogInformation($"Saving new thread ID {threadId} to distributed cache for SessionId: {sessionId}");
                // Save the new thread ID in the distributed cache
                await _cache.SetStringAsync(sessionId, threadId, new DistributedCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromHours(1) // Adjust as necessary
                }, cancellationToken);
            }
            _logger.LogInformation($"Returning thread ID {threadId} for SessionId: {sessionId}");
            return threadId;
        }

        private async Task WaitForRunToComplete(RunResponse run, CancellationToken cancellationToken)
        {
            RunResponse runResponse;
            do
            {
                _logger.LogInformation($"Waiting for run to complete for ThreadId: {run.ThreadId} --- RunId: {run.Id}");
                runResponse = await _apiClient.ThreadsEndpoint.RetrieveRunAsync(run.ThreadId, run.Id, cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            } while (runResponse.Status == RunStatus.InProgress && !cancellationToken.IsCancellationRequested);
        }


        public async Task EndChatSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Ending chat session for sessionId: {sessionId}", sessionId);

                // Clear the session data from the cache
                await _cache.RemoveAsync(sessionId, cancellationToken);

                // Delete the thread
                var threadId = await _cache.GetStringAsync(sessionId, cancellationToken);
                await _apiClient.ThreadsEndpoint.DeleteThreadAsync(threadId);

                _logger.LogInformation("Chat session successfully ended for sessionId: {sessionId}", sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending chat session for sessionId: {sessionId}", sessionId);
                throw;
            }
        }
    }
}