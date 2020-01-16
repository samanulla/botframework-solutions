// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using Luis;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Solutions.Testing.Mocks;
using ExampleBF47Skil.Tests.Utterances;

namespace ExampleBF47Skil.Tests.Utilities
{
    public class SkillTestUtil
    {
        private static Dictionary<string, IRecognizerConvert> _utterances = new Dictionary<string, IRecognizerConvert>
        {
            { SampleDialogUtterances.Trigger, CreateIntent(SampleDialogUtterances.Trigger, ExampleBF47SkilLuis.Intent.Sample) },
        };

        public static MockLuisRecognizer CreateRecognizer()
        {
            var recognizer = new MockLuisRecognizer(defaultIntent: CreateIntent(string.Empty, ExampleBF47SkilLuis.Intent.None));
            recognizer.RegisterUtterances(_utterances);
            return recognizer;
        }

        public static ExampleBF47SkilLuis CreateIntent(string userInput, ExampleBF47SkilLuis.Intent intent)
        {
            var result = new ExampleBF47SkilLuis
            {
                Text = userInput,
                Intents = new Dictionary<ExampleBF47SkilLuis.Intent, IntentScore>()
            };

            result.Intents.Add(intent, new IntentScore() { Score = 0.9 });

            result.Entities = new ExampleBF47SkilLuis._Entities
            {
                _instance = new ExampleBF47SkilLuis._Entities._Instance()
            };

            return result;
        }
    }
}
