using PetFam.Core;
using PetFam.Core.Interactions;

namespace PetFam.Desktop.Ai;

/// <summary>"Talk" in the pie menu (only with an AI): opens the chat box instead of joining the pet's queue.</summary>
public sealed class ChatInteraction : Interaction
{
    public static readonly ChatInteraction Instance = new();

    public override string Id => "chat";
    public override string Label => L.S("Talk", "말 걸기");
    public override string Icon => "💬";

    public override InteractionRunner CreateRunner() => throw new NotSupportedException("Talking opens the chat box; it is never queued.");
}
