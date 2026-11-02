using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class MessageBoxDialog : Window
{
    public MessageBoxDialog()
        : this(string.Empty, string.Empty)
    {
    }

    public MessageBoxDialog(string caption, string message, string? yesText, string? noText)
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
    }

    public MessageBoxDialog(string caption, string message)
    {
        InitializeComponent();
        WindowCaptionColors.Attach(this);

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

        CanMinimize = false;
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
