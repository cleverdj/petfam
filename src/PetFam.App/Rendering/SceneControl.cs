using Avalonia.Controls;
using Avalonia.Media;

namespace PetFam.Desktop.Rendering;

/// <summary>A control that hands its DrawingContext to a painter callback every frame.</summary>
public sealed class SceneControl : Control
{
    public Action<DrawingContext>? Paint { get; set; }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Paint?.Invoke(context);
    }
}
