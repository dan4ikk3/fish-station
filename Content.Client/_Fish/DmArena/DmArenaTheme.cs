using Content.Client.Resources;
using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Maths;

namespace Content.Client._Fish.DmArena;

public static class DmArenaTheme
{
    public static StyleBoxTexture Rounded(Color color, float horizontal = 12, float vertical = 6)
    {
        var texture = IoCManager.Resolve<IResourceCache>().GetTexture("/Textures/Interface/Nano/button.svg.96dpi.png");
        var box = new StyleBoxTexture { Texture = texture, Modulate = color };
        box.SetPatchMargin(StyleBox.Margin.All, 10);
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontal);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, vertical);
        return box;
    }

    public static Font GetFont(bool bold, int size)
    {
        return IoCManager.Resolve<IResourceCache>().NotoStack(bold ? "Bold" : "Regular", size);
    }

    public static readonly Color Accent = Color.FromHex("#4fd1c5");
    public static readonly Color AccentHover = Color.FromHex("#81e6d9");
    public static readonly Color AccentDim = Color.FromHex("#2c7a7b");

    public static readonly Color BodyBg = Color.FromHex("#0d1b2a");
    public static readonly Color TitleBarBg = Color.FromHex("#13283d");
    public static readonly Color CardBg = Color.FromHex("#16293f");
    public static readonly Color CardHoverBg = Color.FromHex("#1d3a58");
    public static readonly Color CardSelectedBg = Color.FromHex("#1f5a3a");

    public static readonly Color CardLockedBg = Color.FromHex("#3a1d26");

    public static readonly Color Border = Color.FromHex("#2f6f8f");

    public static readonly Color ButtonBg = Color.FromHex("#1f4e79");
    public static readonly Color ButtonHoverBg = Color.FromHex("#2a669f");
    public static readonly Color ButtonSelectedBg = Color.FromHex("#2f9e5f");
    public static readonly Color ButtonDisabledBg = Color.FromHex("#4a2229");
    public static readonly Color ButtonDisabledFg = Color.FromHex("#c98b93");
    public static readonly Color ButtonText = Color.FromHex("#eaf4fb");

    public static readonly Color TextMuted = Color.FromHex("#8aa4b8");
    public static readonly Color Cost = Color.FromHex("#f0c36d");
}
