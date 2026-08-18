using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using drive_desktop.ViewModels;
using Ursa.Controls;

namespace drive_desktop.Views;

public partial class Home : Window
{    private const uint WsThickFrame = 0x00040000;
    public static WindowToastManager? GlobalToastManager { get; private set; }


    public Home()
    {
      

        InitializeComponent();

  
    }
    
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        
        GlobalToastManager = new WindowToastManager(this)
        {
            MaxItems = 10
        };
    }
    private async void UploadBorder_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not HomeViewModel vm)
        {
            return;
        }

        var storageItems = e.DataTransfer.TryGetFiles();
        if (storageItems is null)
        {
            return;
        }

        var files = new List<string>();
        var folders = new List<string>();

        foreach (var storageItem in storageItems)
        {
            switch (storageItem)
            {
                case IStorageFile file:
                    files.Add(file.Path.LocalPath);
                    break;
                case IStorageFolder folder:
                    folders.Add(folder.Path.LocalPath);
                    break;
            }
        }

        if (files.Count > 0 || folders.Count > 0)
        {
            await vm.UploadFiles(files, folders);
        }
    }

    
    private void Mask_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (this.DataContext is HomeViewModel vm)
        {
            vm.UpLoadShow = false;
        }
    }
    private void Mask2_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (this.DataContext is HomeViewModel vm)
        {
            vm.FastSearchShow = false;
        }
    }
  
    
    
    
    
}
