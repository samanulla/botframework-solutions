// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Luis;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Solutions;
using Microsoft.Bot.Builder.Solutions.Dialogs;
using Microsoft.Bot.Builder.Solutions.Responses;
using Microsoft.Bot.Schema;
using ExampleBF47Skill.Models;
using ExampleBF47Skill.Responses.Main;
using ExampleBF47Skill.Responses.Shared;
using ExampleBF47Skill.Services;
using Newtonsoft.Json;

namespace ExampleBF47Skill.Dialogs
{
    public class MainDialog : ActivityHandlerDialog
    {
        private BotSettings _settings;
        private BotServices _services;
        private ResponseManager _responseManager;
        private IStatePropertyAccessor<SkillState> _stateAccessor;

        public MainDialog(
            BotSettings settings,
            BotServices services,
            ResponseManager responseManager,
            UserState userState,
            ConversationState conversationState,
            SampleDialog sampleDialog,
            SampleAction sampleAction,
            IBotTelemetryClient telemetryClient)
            : base(nameof(MainDialog), telemetryClient)
        {
            _settings = settings;
            _services = services;
            _responseManager = responseManager;
            TelemetryClient = telemetryClient;

            // Initialize state accessor
            _stateAccessor = conversationState.CreateProperty<SkillState>(nameof(SkillState));

            // Register dialogs
            AddDialog(sampleDialog);
            AddDialog(sampleAction);
        }

        // Runs when the dialog stack is empty, and a new member is added to the conversation. Can be used to send an introduction activity.
        protected override async Task OnMembersAddedAsync(DialogContext innerDc, CancellationToken cancellationToken = default)
        {
            await innerDc.Context.SendActivityAsync(_responseManager.GetResponse(MainResponses.WelcomeMessage));
        }

        protected override async Task OnMessageActivityAsync(DialogContext dc, CancellationToken cancellationToken = default(CancellationToken))
        {
            // get current activity locale
            var locale = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            var localeConfig = _services.CognitiveModelSets[locale];

            // Get skill LUIS model from configuration
            localeConfig.LuisServices.TryGetValue("ExampleBF47Skil", out var luisService);

            if (luisService == null)
            {
                throw new Exception("The specified LUIS Model could not be found in your Bot Services configuration.");
            }
            else
            {
                var result = await luisService.RecognizeAsync<ExampleBF47SkillLuis>(dc.Context, CancellationToken.None);
                var intent = result?.TopIntent().intent;

                switch (intent)
                {
                    case ExampleBF47SkillLuis.Intent.Sample:
                        {
                            await dc.BeginDialogAsync(nameof(SampleDialog));
                            break;
                        }

                    case ExampleBF47SkillLuis.Intent.None:
                        {
                            // No intent was identified, send confused message
                            await dc.Context.SendActivityAsync(_responseManager.GetResponse(SharedResponses.DidntUnderstandMessage));
                            break;
                        }

                    default:
                        {
                            // intent was identified but not yet implemented
                            await dc.Context.SendActivityAsync(_responseManager.GetResponse(MainResponses.FeatureNotAvailable));
                            break;
                        }
                }
            }
        }

        // Runs when an activity with an unknown type is received.
        protected override async Task OnUnhandledActivityTypeAsync(DialogContext innerDc, CancellationToken cancellationToken = default)
        {
            await innerDc.Context.SendActivityAsync(new Activity(type: ActivityTypes.Trace, text: $"Unknown activity was received but not processed."));
        }

        protected override async Task OnDialogCompleteAsync(DialogContext dc, object result = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Retrieve the prior dialogs result if provided to return on the Skill EndOfConversation event.
            ObjectPath.TryGetPathValue<object>(dc.Context.TurnState, TurnPath.LASTRESULT, out object dialogResult);

            var endOfConversation = new Activity(ActivityTypes.EndOfConversation) {
                Code = EndOfConversationCodes.CompletedSuccessfully,
                Value = dialogResult };
            
            await dc.Context.SendActivityAsync(endOfConversation, cancellationToken);
            await dc.EndDialogAsync(result);         
        }

        protected override async Task OnEventActivityAsync(DialogContext dc, CancellationToken cancellationToken = default(CancellationToken))
        {
            var eventActivity = dc.Context.Activity.AsEventActivity();

            if (!string.IsNullOrEmpty(eventActivity.Name))
            {
                switch (eventActivity.Name)
                {
                    // Each Action in the Manifest will have an associated Name which will be on incoming Event activities
                    case "SampleAction":

                        SampleActionInput actionData = null;
                        var eventValue = dc.Context.Activity.Value as string;

                        if (!string.IsNullOrEmpty(eventValue))
                        {
                            actionData = JsonConvert.DeserializeObject<SampleActionInput>(eventValue);
                        }

                        // Invoke the SampleAction dialog passing input data if available
                        await dc.BeginDialogAsync(nameof(SampleAction), actionData);

                        break;

                    default:

                        await dc.Context.SendActivityAsync(new Activity(type: ActivityTypes.Trace, text: $"Unknown Event '{eventActivity.Name ?? "undefined"}' was received but not processed."));

                        break;
                }
            }
            else
            {
                await dc.Context.SendActivityAsync(new Activity(type: ActivityTypes.Trace, text: $"An event with no name was received but not processed."));
                
            }
        }

        protected override async Task<InterruptionAction> OnInterruptDialogAsync(DialogContext dc, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = InterruptionAction.NoAction;

            if (dc.Context.Activity.Type == ActivityTypes.Message)
            {
                // get current activity locale
                var locale = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                var localeConfig = _services.CognitiveModelSets[locale];

                // check general luis intent
                localeConfig.LuisServices.TryGetValue("General", out var luisService);

                if (luisService == null)
                {
                    throw new Exception("The specified LUIS Model could not be found in your Skill configuration.");
                }
                else
                {
                    var luisResult = await luisService.RecognizeAsync<GeneralLuis>(dc.Context, cancellationToken);
                    var topIntent = luisResult.TopIntent();

                    if (topIntent.score > 0.5)
                    {
                        switch (topIntent.intent)
                        {
                            case GeneralLuis.Intent.Cancel:
                                {
                                    result = await OnCancel(dc);
                                    break;
                                }

                            case GeneralLuis.Intent.Help:
                                {
                                    result = await OnHelp(dc);
                                    break;
                                }

                            case GeneralLuis.Intent.Logout:
                                {
                                    result = await OnLogout(dc);
                                    break;
                                }
                        }
                    }
                }
            }

            return result;
        }

        private async Task<InterruptionAction> OnCancel(DialogContext dc)
        {
            await dc.Context.SendActivityAsync(_responseManager.GetResponse(MainResponses.CancelMessage));
            return InterruptionAction.End;
        }

        private async Task<InterruptionAction> OnHelp(DialogContext dc)
        {
            await dc.Context.SendActivityAsync(_responseManager.GetResponse(MainResponses.HelpMessage));
            return InterruptionAction.Resume;
        }

        private async Task<InterruptionAction> OnLogout(DialogContext dc)
        {
            BotFrameworkAdapter adapter;
            var supported = dc.Context.Adapter is BotFrameworkAdapter;
            if (!supported)
            {
                throw new InvalidOperationException("OAuthPrompt.SignOutUser(): not supported by the current adapter");
            }
            else
            {
                adapter = (BotFrameworkAdapter)dc.Context.Adapter;
            }

            await dc.CancelAllDialogsAsync();

            // Sign out user
            var tokens = await adapter.GetTokenStatusAsync(dc.Context, dc.Context.Activity.From.Id);
            foreach (var token in tokens)
            {
                await adapter.SignOutUserAsync(dc.Context, token.ConnectionName);
            }

            await dc.Context.SendActivityAsync(_responseManager.GetResponse(MainResponses.LogOut));

            return InterruptionAction.End;
        }
    }    
}