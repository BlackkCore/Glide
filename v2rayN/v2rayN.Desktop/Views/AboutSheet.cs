using DialogHostAvalonia;
using v2rayN.Desktop.Common;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using VAlign = Avalonia.Layout.VerticalAlignment;

namespace v2rayN.Desktop.Views;

/// <summary>
/// "About Glide": the version, Glide's website, and credit to v2rayN (the project Glide is built on)
/// with a link to its GitHub. Links open in the default browser.
/// </summary>
public sealed class AboutSheet : UserControl
{
    public AboutSheet(Action? checkUpdate = null, bool updateReady = false)
    {
        Width = 460;
        var panel = new StackPanel { Margin = new Thickness(24, 22), Spacing = 12 };

        // Name and version on one line, the version read left to right.
        var header = new DockPanel();
        var version = new TextBlock
        {
            Text = $"v{Utils.GetVersionInfo()}",
            FlowDirection = FlowDirection.LeftToRight,
            VerticalAlignment = VAlign.Center,
        };
        version.Classes.Add("muted");
        DockPanel.SetDock(version, Dock.Right);
        header.Children.Add(version);
        var title = new TextBlock { Text = "Glide" };
        title.Classes.Add("h2");
        header.Children.Add(title);
        panel.Children.Add(header);
        panel.Children.Add(Muted(ResUI.TipAboutTagline));

        // Check update (moved here from the sidebar). Only one sheet can be open, so About closes first.
        if (checkUpdate != null)
        {
            var updateRow = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 4, 0, 0) };
            var update = new Button { Content = ResUI.TbNavCheckUpdate, MinWidth = 160, CornerRadius = new CornerRadius(10) };
            update.Click += (_, _) =>
            {
                Close();
                Dispatcher.UIThread.Post(checkUpdate);
            };
            updateRow.Children.Add(update);
            if (updateReady)
            {
                // Same dot as on the logo, with the words next to it.
                var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8, VerticalAlignment = VAlign.Center };
                dot.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, dot.GetResourceObservable("AppAccentBrush"));
                updateRow.Children.Add(dot);
                updateRow.Children.Add(new TextBlock { Text = ResUI.TipNewUpdateReady, VerticalAlignment = VAlign.Center, TextWrapping = TextWrapping.Wrap });
            }
            panel.Children.Add(updateRow);
        }

        var website = LinkButton(ResUI.TbGlideWebsite, Global.GlideWebsiteUrl, primary: true);
        website.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(website);

        panel.Children.Add(new Separator { Margin = new Thickness(0, 6) });
        panel.Children.Add(Muted(ResUI.TipBuiltOnV2rayN));
        panel.Children.Add(LinkButton(ResUI.TbV2rayNGithub, Global.UpstreamRepoUrl, primary: false));
        // The routing data Glide downloads is built from MaxMind GeoLite data; the licence asks for this notice.
        var geoNotice = Muted(Global.GeoLiteAttribution);
        geoNotice.FlowDirection = FlowDirection.LeftToRight;
        geoNotice.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(geoNotice);

        var close = new Button { Content = ResUI.TbClose, HorizontalAlignment = HAlign.Right, MinWidth = 96, IsCancel = true };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = panel;
    }

    /// <summary>Shows the sheet (nothing happens if another sheet is open).</summary>
    public static void Open(Action? checkUpdate = null, bool updateReady = false)
    {
        if (DialogHost.IsDialogOpen(null))
        {
            return;
        }
        var sheet = new AboutSheet(checkUpdate, updateReady);
        _ = DialogHost.Show(sheet);
        AppMotion.PlayEntrance(sheet, 6, AppMotion.RevealMs);
    }

    // A button that opens the address in the browser; the address itself is its tooltip.
    private static Button LinkButton(string text, string url, bool primary)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HAlign.Left,
            MinWidth = 160,
            CornerRadius = new CornerRadius(10),
        };
        if (primary)
        {
            button.Classes.Add("Primary");
            button.Bind(Button.ThemeProperty, button.GetResourceObservable("SolidButton"));
        }
        ToolTip.SetTip(button, url);
        button.Click += (_, _) =>
        {
            try
            {
                ProcUtils.ProcessStart(url);
            }
            catch (Exception ex)
            {
                Logging.SaveLog("AboutSheet", ex);
            }
        };
        return button;
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.Classes.Add("muted");
        return block;
    }

    private static void Close()
    {
        if (DialogHost.IsDialogOpen(null))
        {
            DialogHost.Close(null);
        }
    }
}
