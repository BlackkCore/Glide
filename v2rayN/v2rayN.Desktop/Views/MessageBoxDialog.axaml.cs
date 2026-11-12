using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class MessageBoxDialog : Window
{
    public MessageBoxDialog()
        : this(string.Empty, string.Empty)
    {
    }

    public MessageBoxDialog(string caption, string message, string? yesText, string? noText, bool destructive = false)
        : this(caption, message)
    {
        if (yesText.IsNotEmpty())
        {
            btnYes.Content = yesText;
        }
        if (noText.IsNotEmpty())
        {
            btnNo.Content = noText;
        }
        if (destructive)
        {
            // Red action, and Enter means Cancel: deleting takes a deliberate click (or Tab + Enter).
            btnYes.Classes.Add("Danger");
            btnYes.IsDefault = false;
            btnNo.IsDefault = true;
            Opened += (_, _) => btnNo.Focus();
        }
    }

    public MessageBoxDialog(string caption, string message)
    {
        InitializeComponent();
        WindowCaptionColors.Attach(this);
        Opened += (_, _) => AppMotion.PlayEntrance(Content as Visual, 8, 220);

        if (Design.IsDesignMode)
        {
            caption = "Design Caption";
            message = "Design Message";
        }

        Title = caption;
        txtMessage.Text = message;

        // Persian and other right-to-left text needs RTL layout, or words break and lines jumble.
        // Decided from the text itself, since it may not match the window's current culture.
        if (message.Any(c => c is >= '֐' and <= 'ࣿ'))
        {
            FlowDirection = FlowDirection.RightToLeft;
        }

        btnYes.Click += BtnYes_Click;
        btnNo.Click += BtnNo_Click;

        Opened += MessageBoxDialog_Opened;

        CanMinimize = false;
    }

    private void MessageBoxDialog_Opened(object? sender, EventArgs e)
    {
        btnYes.Focus();
    }

    private void BtnYes_Click(object? sender, RoutedEventArgs e)
    {
        Close(ButtonResult.Yes);
    }

    private void BtnNo_Click(object? sender, RoutedEventArgs e)
    {
        Close(ButtonResult.No);
    }
}
