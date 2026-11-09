using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace UniversalCooldownUI
{
    /// <summary>
    /// Server data over the game chat.
    ///
    /// The client cannot read files from the server machine, so the tab data is requested
    /// with the in-game command <c>.ucui d</c> and delivered in machine readable chat lines:
    ///
    ///   UCUI|&lt;part&gt;|&lt;total&gt;|&lt;payload&gt;
    ///
    /// These lines are consumed and destroyed before the chat is drawn, so players never
    /// see them.
    /// </summary>
    internal static class ChatSync
    {
        const string ProtocolPrefix = "UCUI|";
        const string RequestCommand = ".ucui d";

        static World _world;
        static Entity _localUser;
        static Entity _localCharacter;
        static bool _resolved;

        static float _nextRequest;

        static readonly List<string> Parts = new();
        static int _expected;
        static int _received;

        internal static void Install(Harmony harmony)
        {
            try
            {
                Type chat = AccessTools.TypeByName("ProjectM.UI.ClientChatSystem");
                MethodInfo onUpdate = chat == null ? null : AccessTools.Method(chat, "OnUpdate");

                if (onUpdate == null)
                {
                    Plugin.L.LogWarning("[ChatSync] ClientChatSystem.OnUpdate not found - chat sync disabled");
                    return;
                }

                harmony.Patch(onUpdate, prefix: new HarmonyMethod(
                    typeof(ChatSync).GetMethod(nameof(OnChatUpdate), BindingFlags.Static | BindingFlags.NonPublic)));

                Plugin.L.LogInfo("[ChatSync] chat hooked");
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[ChatSync] install: " + e.Message);
            }
        }

        internal static void SetWorld(World world)
        {
            if (_world == null && world != null) _world = world;
        }

        static float _nextAuto;

        /// <summary>Keeps rates fresh even when the tab is never opened (tooltips need them).</summary>
        internal static void AutoPoll()
        {
            if (_world == null) return;
            if (Time.time < _nextAuto) return;

            _nextAuto = Time.time + 120f;
            Request();
        }

        /// <summary>Asks the server for fresh tab data (throttled).</summary>
        internal static void Request()
        {
            if (_world == null) return;
            if (Time.time < _nextRequest) return;

            _nextRequest = Time.time + 8f;
            Send(RequestCommand);
        }

        static void ResolveLocalEntities(EntityManager em)
        {
            if (_resolved) return;
            _resolved = true;

            try
            {
                var userQuery = em.CreateEntityQuery(ComponentType.ReadOnly<LocalUser>());
                var users = userQuery.ToEntityArray(Allocator.Temp);

                try
                {
                    if (users.Length > 0) _localUser = users[0];
                }
                finally
                {
                    users.Dispose();
                    userQuery.Dispose();
                }

                var characterQuery = em.CreateEntityQuery(ComponentType.ReadOnly<LocalCharacter>());
                var characters = characterQuery.ToEntityArray(Allocator.Temp);

                try
                {
                    if (characters.Length > 0) _localCharacter = characters[0];
                }
                finally
                {
                    characters.Dispose();
                    characterQuery.Dispose();
                }
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[ChatSync] local entities: " + e.Message);
            }
        }

        static void Send(string text)
        {
            try
            {
                var em = _world.EntityManager;
                ResolveLocalEntities(em);

                if (_localUser == Entity.Null || _localCharacter == Entity.Null) return;

                var components = new ComponentType[]
                {
                    ComponentType.ReadOnly<FromCharacter>(),
                    ComponentType.ReadOnly<NetworkEventType>(),
                    ComponentType.ReadOnly<SendNetworkEventTag>(),
                    ComponentType.ReadOnly<ChatMessageEvent>(),
                };

                var message = new ChatMessageEvent
                {
                    MessageText = text,
                    MessageType = (ChatMessageType)4,      // local chat: server treats it as typed by the player
                    ReceiverEntity = em.GetComponentData<NetworkId>(_localUser),
                };

                var eventType = new NetworkEventType
                {
                    IsAdminEvent = false,
                    EventId = NetworkEvents.EventId_ChatMessageEvent,
                    IsDebugEvent = false,
                };

                Entity entity = em.CreateEntity(components);
                em.SetComponentData(entity, new FromCharacter { Character = _localCharacter, User = _localUser });
                em.SetComponentData(entity, eventType);
                em.SetComponentData(entity, message);

                Plugin.L.LogInfo("[ChatSync] data requested");
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[ChatSync] send: " + e.Message);
            }
        }

        static void OnChatUpdate()
        {
            try
            {
                if (_world == null || !_world.IsCreated) return;

                var em = _world.EntityManager;
                var query = em.CreateEntityQuery(ComponentType.ReadOnly<ChatMessageServerEvent>());
                var entities = query.ToEntityArray(Allocator.Temp);

                try
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity entity = entities[i];
                        if (!em.HasComponent<ChatMessageServerEvent>(entity)) continue;

                        var message = em.GetComponentData<ChatMessageServerEvent>(entity);
                        string text = message.MessageText.ToString();

                        if (string.IsNullOrEmpty(text)) continue;

                        if (text.StartsWith(ProtocolPrefix, StringComparison.Ordinal))
                        {
                            HandlePart(text);

                            // hide our protocol lines from the chat window
                            em.DestroyEntity(entity);
                            continue;
                        }

                        // hide the echo of our own request command
                        if (text.Trim().StartsWith(RequestCommand, StringComparison.OrdinalIgnoreCase))
                        {
                            em.DestroyEntity(entity);
                        }
                    }
                }
                finally
                {
                    entities.Dispose();
                    query.Dispose();
                }
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[ChatSync] read: " + e.Message);
            }
        }

        static void HandlePart(string text)
        {
            try
            {
                // UCUI|part|total|payload
                string[] parts = text.Split(new[] { '|' }, 4);
                if (parts.Length < 4) return;

                if (!int.TryParse(parts[1], out int part)) return;
                if (!int.TryParse(parts[2], out int total)) return;

                if (total <= 0 || part <= 0) return;

                if (total != _expected || part == 1)
                {
                    Parts.Clear();
                    _expected = total;
                    _received = 0;
                }

                Parts.Add(parts[3]);
                _received++;

                if (_received < _expected) return;

                string payload = string.Concat(Parts).Replace('~', '\n');
                Parts.Clear();
                _expected = 0;
                _received = 0;

                BuffsTab.OnRemotePayload(payload);
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[ChatSync] part: " + e.Message);
            }
        }
    }
}
