// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Azure;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Builder.Solutions.Feedback;
using Microsoft.Bot.Builder.Solutions.Middleware;
using Microsoft.Bot.Builder.Solutions.Responses;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Schema;
using ExampleBF47Skill.Responses.Shared;
using ExampleBF47Skill.Services;

namespace ExampleBF47Skill.Bots
{
    public class DefaultAdapter : BotFrameworkHttpAdapter
    {
        public DefaultAdapter(
            BotSettings settings,
            ICredentialProvider credentialProvider,
            ConversationState conversationState,
            IBotTelemetryClient telemetryClient,
            ResponseManager responseManager)
            : base(credentialProvider)
        {
            OnTurnError = async (context, exception) =>
            {
                CultureInfo.CurrentUICulture = new CultureInfo(context.Activity.Locale ?? "en-us");
                await context.SendActivityAsync(responseManager.GetResponse(SharedResponses.ErrorMessage));
                await context.SendActivityAsync(new Activity(type: ActivityTypes.Trace, text: $"Skill Error: {exception.Message} | {exception.StackTrace}"));
                telemetryClient.TrackException(exception);

                // Send and EndOfConversation activity to the skill caller with the error to end the conversation
                // and let the caller decide what to do.
                var endOfConversation = Activity.CreateEndOfConversationActivity();
                endOfConversation.Code = "SkillError";
                endOfConversation.Text = exception.Message;
                await context.SendActivityAsync(endOfConversation);
            };

            // Uncomment the following line for local development without Azure Storage
            // Use(new TranscriptLoggerMiddleware(new MemoryTranscriptStore()));
            Use(new TranscriptLoggerMiddleware(new AzureBlobTranscriptStore(settings.BlobStorage.ConnectionString, settings.BlobStorage.Container)));
            Use(new TelemetryLoggerMiddleware(telemetryClient, logPersonalInformation: true));
            Use(new ShowTypingMiddleware());
            Use(new FeedbackMiddleware(conversationState, telemetryClient));
            Use(new SetLocaleMiddleware(settings.DefaultLocale ?? "en-us"));
            Use(new EventDebuggerMiddleware());
        }
    }
}