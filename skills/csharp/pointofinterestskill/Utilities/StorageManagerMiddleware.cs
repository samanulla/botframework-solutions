// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Moq;
using Newtonsoft.Json;

namespace PointOfInterestSkill.Utilities
{
    public class StorageManagerMiddleware : IMiddleware
    {
        private const string ParametersPrefix = "/storage:";
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.All,
            ReferenceLoopHandling = ReferenceLoopHandling.Error,
        };

        private readonly IStorage _storage;
        private readonly BotStateSet _botStateSet;
        private readonly string _savePath;
        private readonly List<MethodInfo> _getStorageKeys = new List<MethodInfo>();

        public StorageManagerMiddleware(
            IStorage storage,
            BotStateSet botStateSet,
            string savePath)
        {
            _storage = storage;
            _botStateSet = botStateSet;
            _savePath = savePath;

            foreach (var state in _botStateSet.BotStates)
            {
                // TODO protected method
                _getStorageKeys.Add(state.GetType().GetMethod("GetStorageKey", BindingFlags.NonPublic | BindingFlags.Instance, null, CallingConventions.Any, new Type[] { typeof(ITurnContext) }, null));
            }
        }

        private enum CommandType
        {
            Load = 0,
            Clear = 1,
        }

        public async Task OnTurnAsync(ITurnContext turnContext, NextDelegate next, CancellationToken cancellationToken = default(CancellationToken))
        {
            var activity = turnContext.Activity;

            if (activity.Type == ActivityTypes.Message && !string.IsNullOrEmpty(activity.Text))
            {
                var text = activity.Text;

                if (!string.IsNullOrEmpty(text) && text.StartsWith(ParametersPrefix))
                {
                    var json = text.Split(new string[] { ParametersPrefix }, StringSplitOptions.None)[1];
                    var parameters = JsonConvert.DeserializeObject<Parameters>(json);
                    if (parameters.Command == CommandType.Clear)
                    {
                        Directory.Delete(_savePath, true);
                        await turnContext.SendActivityAsync($"Delete {_savePath}");
                        return;
                    }
                    else if (parameters.Command == CommandType.Load)
                    {
                        var file = Path.Combine(_savePath, parameters.Id);
                        if (!File.Exists(file))
                        {
                            await turnContext.SendActivityAsync($"{file} does not exist!");
                            return;
                        }

                        var data = JsonConvert.DeserializeObject<Data>(File.ReadAllText(file), SerializerSettings);

                        async Task<bool> CheckSame(string v0, string v1, string type)
                        {
                            if (v0 != v1)
                            {
                                await turnContext.SendActivityAsync($"Current {type} {v0} != loaded {v1}!");
                                return false;
                            }

                            return true;
                        }

                        if (!parameters.Force)
                        {
                            if (!await CheckSame(turnContext.Activity.ChannelId, data.ChannelId, "ChannelId"))
                            {
                                return;
                            }

                            if (!await CheckSame(turnContext.Activity.From.Id, data.UserId, "UserId"))
                            {
                                return;
                            }

                            if (!await CheckSame(turnContext.Activity.Conversation.Id, data.ConversationId, "ConversationId"))
                            {
                                return;
                            }
                        }

                        var newAllData = new Dictionary<string, object>();
                        var turnContextMock = new Mock<ITurnContext>();
                        turnContextMock.Setup(tc => tc.Activity).Returns(new Activity { ChannelId = data.ChannelId, From = new ChannelAccount { Id = data.UserId }, Conversation = new ConversationAccount { Id = data.ConversationId } });
                        for (int i = 0; i < _getStorageKeys.Count; ++i)
                        {
                            var loadedKey = (string)_getStorageKeys[i].Invoke(_botStateSet.BotStates[i], new object[] { turnContextMock.Object });
                            var currentKey = (string)_getStorageKeys[i].Invoke(_botStateSet.BotStates[i], new object[] { turnContext });
                            if (data.AllData.ContainsKey(loadedKey))
                            {
                                newAllData[currentKey] = data.AllData[loadedKey];
                            }
                        }

                        await _storage.WriteAsync(newAllData, cancellationToken);
                        await turnContext.SendActivityAsync($"Loaded {file}.");
                        return;
                    }
                }
            }

            await next(cancellationToken).ConfigureAwait(false);

            var saveData = new Data
            {
                ChannelId = turnContext.Activity.ChannelId,
                UserId = turnContext.Activity.From.Id,
                ConversationId = turnContext.Activity.Conversation.Id
            };
            var keys = new List<string>();
            for (int i = 0; i < _getStorageKeys.Count; ++i)
            {
                keys.Add((string)_getStorageKeys[i].Invoke(_botStateSet.BotStates[i], new object[] { turnContext }));
            }

            saveData.AllData = await _storage.ReadAsync(keys.ToArray(), cancellationToken);
            var name = Guid.NewGuid().ToString();
            File.WriteAllText(Path.Join(_savePath, name), JsonConvert.SerializeObject(saveData, SerializerSettings));
            await turnContext.SendActivityAsync(new Activity(type: ActivityTypes.Trace, text: $"Saved {name} with {saveData.ChannelId}, {saveData.UserId}, {saveData.ConversationId}."));
        }

        private class Parameters
        {
            public CommandType Command { get; set; } = CommandType.Load;

            public string Id { get; set; }

            public bool Force { get; set; } = false;
        }

        private class Data
        {
            // TODO assume only these three effect storage key
            public string ChannelId { get; set; }

            public string UserId { get; set; }

            public string ConversationId { get; set; }

            public IDictionary<string, object> AllData { get; set; }
        }
    }
}
