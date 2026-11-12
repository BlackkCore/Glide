namespace v2rayN.Desktop.Views;

public partial class CheckUpdateView : ReactiveUserControl<CheckUpdateViewModel>
{
    public CheckUpdateView()
    {
        InitializeComponent();
        btnCloseUpdates.Click += (_, _) =>
        {
            if (DialogHostAvalonia.DialogHost.IsDialogOpen(null))
            {
                DialogHostAvalonia.DialogHost.Close(null);
            }
        };

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.CheckUpdateModels, v => v.lstCheckUpdates.ItemsSource).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.EnableUpdateViaProxy, v => v.togEnableUpdateViaProxy.IsChecked).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.CheckOnlyCmd, v => v.btnCheckOnly).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.CheckUpdateCmd, v => v.btnCheckUpdate).DisposeWith(disposables);
        });
    }
}
