using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfPath = System.Windows.Shapes.Path;
using WpfTabControl = System.Windows.Controls.TabControl;

namespace Daynote.App.Showcase;

internal static partial class PrimitiveFixtureFactory
{
    private static readonly (string Name, string Geometry)[] MarkdownCommands =
    [
        ("Bold", "Daynote.Icon.Geometry.Bold"),
        ("Italic", "Daynote.Icon.Geometry.Italic"),
        ("Bulleted list", "Daynote.Icon.Geometry.BulletedList"),
        ("Numbered list", "Daynote.Icon.Geometry.NumberedList"),
        ("Inline code", "Daynote.Icon.Geometry.InlineCode")
    ];

    private static TextBlock RoleText(string content, string role, string name)
    {
        var text = new TextBlock { Text = content };
        ShowcaseResources.Style(text, $"Daynote.Style.Type.{role}");
        AutomationProperties.SetName(text, name);
        return text;
    }

    private static WpfButton IconButton(string name, string geometry, string style = "Daynote.Style.IconButton.Ghost")
    {
        var icon = new WpfPath();
        ShowcaseResources.Style(icon, "Daynote.Style.IconPresenter");
        icon.SetResourceReference(WpfPath.DataProperty, geometry);
        icon.IsHitTestVisible = false;

        var button = new WpfButton { Content = icon };
        ShowcaseResources.Style(button, style);
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        return button;
    }

    private static WpfTabControl TabStrip(string name)
    {
        var tabs = new WpfTabControl();
        ShowcaseResources.Style(tabs, "Daynote.Style.TabStrip");
        AutomationProperties.SetName(tabs, name);
        return tabs;
    }

    private static TextBlock TabHeader(string title)
    {
        var header = RoleText(title, "Label", $"{title} full tab title");
        header.TextWrapping = TextWrapping.NoWrap;
        header.TextTrimming = TextTrimming.CharacterEllipsis;
        return header;
    }

    private static TextBlock NoteTabHeader(string title, bool wraps)
    {
        var header = RoleText(title, "Label", title);
        header.SetResourceReference(FrameworkElement.MaxWidthProperty, "Daynote.Size.NoteTab.TitleMax");
        if (wraps)
        {
            header.TextWrapping = TextWrapping.Wrap;
            header.TextTrimming = TextTrimming.None;
            header.SetResourceReference(FrameworkElement.MaxHeightProperty, "Daynote.Size.NoteTab.TitleMaxHeight");
        }
        else
        {
            header.TextWrapping = TextWrapping.NoWrap;
            header.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        return header;
    }

    private static TextBlock StatusText(string content, string name)
    {
        var status = RoleText(content, "Status", name);
        status.TextWrapping = TextWrapping.Wrap;
        status.TextTrimming = TextTrimming.None;
        status.SetResourceReference(FrameworkElement.MaxHeightProperty, "Daynote.Size.StatusText.MaxHeight");
        return status;
    }

    private static string StressTitle(ShowcaseStress stress) => stress switch
    {
        ShowcaseStress.Cjk => "입력기 조합 기록 · Daynote",
        ShowcaseStress.Long => "A deliberately long note title wraps across two lines while every adjacent action stays visible",
        ShowcaseStress.Unbroken => "NOTE_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_abcdefghijklmnopqrstuvwxyz_0123456789",
        _ => "Note 1"
    };

    private static string StressQuery(ShowcaseStress stress) => stress switch
    {
        ShowcaseStress.Cjk => "한글 Latin 통합검색",
        ShowcaseStress.Long => "deliberately long search query remains bounded inside the fixed command region",
        ShowcaseStress.Unbroken => "QUERY_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_abcdefghijklmnopqrstuvwxyz_0123456789",
        _ => "Search"
    };

    private static string StressStatus(ShowcaseSelection selection) => selection.Stress switch
    {
        ShowcaseStress.Cjk => "입력기 조합 상태를 확인합니다.\n후보 창은 OS 수준 QA에서 확인합니다.",
        ShowcaseStress.Long => "Save failed after a long wait; the note remains safe, and Retry stays available when the connection returns.",
        ShowcaseStress.Unbroken => "STATUS_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_abcdefghijklmnopqrstuvwxyz_0123456789",
        _ => StateMessage(selection)
    };

    private static string StressEditorBody(ShowcaseSelection selection) => selection.Stress switch
    {
        ShowcaseStress.Cjk =>
            "# 입력기 조합 시험\n\n안녕하세요 Daynote.\n한글과 Latin 문장을 함께 씁니다.\n" +
            "줄 높이와 안전한 줄바꿈을 검증합니다.\n\n정적 fixture는 커밋된 문장만 보여 줍니다.\n" +
            "후보 창 상호작용은 OS 수준 QA에서 확인합니다.",
        ShowcaseStress.Long =>
            "# A deliberately long daily note\n\n" +
            "This deliberately long paragraph verifies that authored Markdown wraps inside the readable editor measure, " +
            "keeps the fixed toolbar visible, and never expands the surrounding calendar or clipboard rails.",
        ShowcaseStress.Unbroken =>
            "https://daynote.invalid/verification/ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_abcdefghijklmnopqrstuvwxyz_0123456789",
        _ => "Plain Markdown editor remains the dominant open paper plane."
    };

    private static string StressClipboardPreview(ShowcaseStress stress) => stress switch
    {
        ShowcaseStress.Cjk => "목요일 기록은\n입력기 조합 중에도\n안전하게 유지됩니다.\n한글과 Latin을 씁니다.",
        _ => ShowcaseUi.StressText(stress)
    };

    private static string StressClipboardStatus(ShowcaseSelection selection) => selection.Stress switch
    {
        ShowcaseStress.Cjk => "입력기 상태를 확인합니다.\n후보 창은 OS 수준에서\n따로 확인합니다.",
        _ => StressStatus(selection)
    };
}
