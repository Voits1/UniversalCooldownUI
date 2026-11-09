using System;
using Bloodcraft.Services;
using Bloodcraft.Systems;
using ProjectM;
using Unity.Entities;
using VampireCommandFramework;

namespace Bloodcraft.Commands;

/// <summary>
/// Chat command used by the Universal Cooldown UI client mod.
///
/// The client cannot read files from the server machine, so it asks for the tab data
/// over chat. The reply is a sequence of machine readable lines:
///
///   UCUI|&lt;part&gt;|&lt;total&gt;|&lt;payload&gt;
///
/// where payload is the same text that the service writes to disk, with '\n' replaced by '~'.
/// The client swallows these lines (it destroys them before the chat is drawn).
/// </summary>
[CommandGroup(name: "ucui", shortHand: "uc")]
internal static class UniversalCooldownCommands
{
    const int ChunkSize = 200;
    const string Prefix = "UCUI";

    [Command(name: "data", shortHand: "d", adminOnly: false, usage: ".ucui d",
             description: "Sends Universal Cooldown UI data to your client.")]
    public static void SendData(ChatCommandContext ctx)
    {
        try
        {
            ulong steamId = ctx.Event.User.PlatformId;
            Entity player = ctx.Event.SenderCharacterEntity;

            string payload = BuffsTabService.BuildText(player, steamId);
            string flat = (payload ?? string.Empty).Replace('\n', '~');

            int total = (flat.Length + ChunkSize - 1) / ChunkSize;
            if (total == 0) total = 1;

            for (int i = 0; i < total; i++)
            {
                int length = Math.Min(ChunkSize, flat.Length - i * ChunkSize);
                string part = length > 0 ? flat.Substring(i * ChunkSize, length) : string.Empty;

                ctx.Reply($"{Prefix}|{i + 1}|{total}|{part}");
            }
        }
        catch (Exception e)
        {
            Core.Log.LogWarning("[BuffsTab] chat data: " + e.Message);
        }
    }
}
