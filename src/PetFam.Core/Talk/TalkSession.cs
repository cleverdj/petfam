using PetFam.Core.Pets;
using PetFam.Core.Social;

namespace PetFam.Core.Talk;

/// <summary>
/// Two pets act out a conversation: the first walks over, they face each other, and the lines go back and forth in
/// speech bubbles (each held long enough to read). A warm talk brings them a little closer, a tense one sets them back.
/// </summary>
public sealed class TalkSession : SocialSession
{
    private enum Phase { Approach, Talk, Linger }

    public const float FriendshipPerTone = 2f;

    private readonly Dialogue _dialogue;
    private Phase _phase;
    private int _line = -1;
    private float _lineFor;

    public TalkSession(Dialogue dialogue) : base(dialogue.A, dialogue.B) => _dialogue = dialogue;

    public Dialogue Dialogue => _dialogue;

    public override string Name => "talk:" + _phase;

    protected override float Limit => 90f;

    /// <summary>How long a bubble stays: long enough to read it.</summary>
    public static float ReadSeconds(string text) =>
        Math.Clamp(1.4f + text.Length * (L.Ko ? 0.11f : 0.06f), 2.2f, 5.5f);

    protected override void Step(SocialContext ctx, float dt)
    {
        var a = Initiator;
        var b = Partner;
        switch (_phase)
        {
            case Phase.Approach:
                Face(b, a);
                if (b.Animation is not ("sit" or "lie")) b.SetAnimation("idle");
                if (MoveTo(ctx, a, Beside(ctx, b, a.Position, 85f), dt, 1f, "walk") || T > 10f)
                {
                    a.SetAnimation("idle");
                    Next(ref _phase, Phase.Talk);
                }
                break;

            case Phase.Talk:
                Face(a, b);
                Face(b, a);
                if (T < _lineFor) break;
                _line++;
                if (_line >= _dialogue.Lines.Count)
                {
                    if (_dialogue.Tone != 0) ctx.Social.AddFriendship(a, b, _dialogue.Tone * FriendshipPerTone);
                    Next(ref _phase, Phase.Linger);
                    break;
                }
                var line = _dialogue.Lines[_line];
                var seconds = ReadSeconds(line.Text);
                line.Speaker.Say(line.Text, seconds);
                line.Speaker.SetAnimation(line.Act ?? (line.Speaker.Animation is "sit" or "lie" ? line.Speaker.Animation : "idle"));
                _lineFor = seconds + 0.4f;
                T = 0f;
                break;

            case Phase.Linger:
                if (T > 1.5f) End();
                break;
        }
    }
}
