using Avalonia.Platform.Storage;
using v2rayN.Desktop.Manager;
using v2rayN.Desktop.Views;

namespace v2rayN.Desktop.Common;

internal class UI
{
    private static readonly string caption = Global.AppDisplayName;

    public static async Task<ButtonResult> ShowYesNo(string msg)
    {
        return await ShowYesNo(msg, null, null);
    }

    /// <summary>
    /// <paramref name="destructive"/>: the Yes button is red and Cancel is the default, so Enter never
    /// deletes by accident.
    /// </summary>
    public static async Task<ButtonResult> ShowYesNo(string msg, string? yesText, string? noText, bool destructive = false)
    {
        var owner = WindowDialog.TryGetOwnerWindow();
        var box = new MessageBoxDialog(caption, msg, yesText, noText, destructive);
        var result = await box.ShowDialog<ButtonResult>(owner);
        return result == ButtonResult.Yes ? ButtonResult.Yes : ButtonResult.No;
    }

    public static async Task<string?> OpenFileDialog(FilePickerFileType? filter)
    {
        var sp = GetStorageProvider();
        if (sp is null)
        {
            return null;
        }

        // Start async operation to open the dialog.
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = filter is null ? [FilePickerFileTypes.All, FilePickerFileTypes.ImagePng] : [filter]
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task<string?> SaveFileDialog(string filter)
    {
        var sp = GetStorageProvider();
        if (sp is null)
        {
            return null;
        }

        // Start async operation to open the dialog.
        var files = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
        });

        return files?.TryGetLocalPath();
    }

    public static async Task<string?> OpenFolderDialog(string? title)
    {
        var sp = GetStorageProvider();
        if (sp is null)
        {
            return null;
        }

        var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = title
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private static IStorageProvider? GetStorageProvider()
    {
        var owner = WindowDialog.TryGetOwnerWindow();
        var topLevel = TopLevel.GetTopLevel(owner);
        return topLevel?.StorageProvider;
    }
}
