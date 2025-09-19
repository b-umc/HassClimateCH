using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace HomeAssistantClimate.Gateway
{
    internal static class HomeAssistantMessageFactory
    {
        public static JObject CreateAuthMessage(string token)
        {
            return new JObject
            {
                ["type"] = "auth",
                ["access_token"] = token
            };
        }

        public static JObject CreateSubscribeStateChangesMessage(int id)
        {
            return new JObject
            {
                ["id"] = id,
                ["type"] = "subscribe_events",
                ["event_type"] = "state_changed"
            };
        }

        public static JObject CreateGetStatesMessage(int id)
        {
            return new JObject
            {
                ["id"] = id,
                ["type"] = "get_states"
            };
        }

        public static JObject CreateCallServiceMessage(int id, string entityId, string service, IDictionary<string, JToken> data)
        {
            var payload = new JObject
            {
                ["id"] = id,
                ["type"] = "call_service",
                ["domain"] = "climate",
                ["service"] = service,
                ["target"] = new JObject { ["entity_id"] = entityId },
                ["service_data"] = new JObject()
            };

            if (data != null)
            {
                foreach (var kvp in data)
                {
                    payload.Value<JObject>("service_data")[kvp.Key] = kvp.Value;
                }
            }

            return payload;
        }
    }
}
