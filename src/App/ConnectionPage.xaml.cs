﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.FactoryOrchestrator.Core;
using System;
using System.Net;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Storage;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using System.Threading.Tasks;
using System.Threading;
using Windows.ApplicationModel.Resources;
using System.Globalization;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace Microsoft.FactoryOrchestrator.UWP
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class ConnectionPage : Page, IDisposable
    {
        public ConnectionPage()
        {
            this.InitializeComponent();
            ((App)Application.Current).Client = new FactoryOrchestratorUWPClient(IPAddress.Loopback, 45684);
            ((App)Application.Current).Client.OnConnected += ((App)Application.Current).OnIpcConnected;
            ((App)Application.Current).OnConnectionPage = true;
            connectionSem = new SemaphoreSlim(1, 1);
            localSettings = ApplicationData.Current.LocalSettings;
        }
        
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e == null)
            {
                lastNavTag = null;
            }
            else
            {
                lastNavTag = e.Parameter as string;
            }

            if (localSettings.Values.ContainsKey("lastIp"))
            {
                IpTextBox.Text = (string)localSettings.Values["lastIp"];
                ConnectButton.IsEnabled = true;
            }

            Task.Run(async ()  =>
            {
                // Attempt to connect to localhost every 2 seconds in a background task
                while (!((App)Application.Current).Client.IsConnected)
                {
                    await connectionSem.WaitAsync();
                    try
                    {
                        // Ensure we are not connected, user connection might have succeeded while waiting for semaphore
                        if (!((App)Application.Current).Client.IsConnected)
                        {
                            if (((App)Application.Current).Client.IpAddress != IPAddress.Loopback)
                            {
                                // User connection attempted and failed. Recreate Client
                                ((App)Application.Current).Client = new FactoryOrchestratorUWPClient(IPAddress.Loopback, 45684);
                                ((App)Application.Current).Client.OnConnected += ((App)Application.Current).OnIpcConnected;
                            }

                            if (await ((App)Application.Current).Client.TryConnect(((App)Application.Current).IgnoreVersionMismatch))
                            {
                                ((App)Application.Current).OnConnectionPage = false;
                                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                                {
                                    this.Frame.Navigate(typeof(MainPage), lastNavTag);
                                });
                            }
                        }
                    }
                    finally
                    {
                        connectionSem.Release();
                    }

                    if (!((App)Application.Current).Client.IsConnected)
                    {
                        await Task.Delay(2000);
                    }
                }
            });
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            bool validIp = false;

            validIp = IPAddress.TryParse(IpTextBox.Text, out var ip);

            if (validIp)
            {
                ConnectButton.IsEnabled = false;
                await connectionSem.WaitAsync();
                try
                {
                    ((App)Application.Current).Client = new FactoryOrchestratorUWPClient(ip, 45684);
                    ((App)Application.Current).Client.OnConnected += ((App)Application.Current).OnIpcConnected;
                    if (await ((App)Application.Current).Client.TryConnect(((App)Application.Current).IgnoreVersionMismatch))
                    {
                        ((App)Application.Current).OnConnectionPage = false;
                        this.Frame.Navigate(typeof(MainPage), lastNavTag);
                        localSettings.Values["lastIp"] = IpTextBox.Text;
                    }
                    else
                    {
                        ShowConnectFailure(ip.ToString());
                    }
                }
                finally
                {
                    connectionSem.Release();
                    ConnectButton.IsEnabled = true;
                }
            }
        }

        private async void ShowConnectFailure(string ipStr)
        {
            ContentDialog failedConnectDialog = new ContentDialog
            {
                Title = resourceLoader.GetString("BadIpTitle"),
                Content = string.Format(CultureInfo.CurrentCulture, resourceLoader.GetString("BadIpContent"), ipStr),
                CloseButtonText = resourceLoader.GetString("Ok")
            };

            _ = await failedConnectDialog.ShowAsync();
        }

        private void IpTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if(!string.IsNullOrWhiteSpace(IpTextBox.Text))
            {
                ConnectButton.IsEnabled = true;
            }
        }

        private void IpTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (!String.IsNullOrWhiteSpace(IpTextBox.Text))
                {
                    ConnectButton_Click(null, null);
                }
            }
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            this.Frame.Navigate(typeof(AboutPage));
        }

        private void ConfirmExit_Click(object sender, RoutedEventArgs e)
        {
            ((App)Application.Current).Exit();
        }

        private async void ValidateXMLButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add(".xml");
            Windows.Storage.StorageFolder localFolder = ApplicationData.Current.LocalFolder;
            StorageFile tempXml = await localFolder.CreateFileAsync("temp.xml", CreationCollisionOption.ReplaceExisting);

            Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                var path = file.Path;
                await file.CopyAndReplaceAsync(tempXml);

                try
                {
                    FactoryOrchestratorXML.Load(tempXml.Path);

                    ContentDialog successLoadDialog = new ContentDialog
                    {
                        Title = resourceLoader.GetString("XmlValidatedTitle"),
                        Content = string.Format(CultureInfo.CurrentCulture, resourceLoader.GetString("XmlValidatedContent"), path),
                        CloseButtonText = resourceLoader.GetString("Ok")
                    };

                    _ = await successLoadDialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    var msg = ex.AllExceptionsToString().Replace(tempXml.Path, path, StringComparison.OrdinalIgnoreCase);
                    ContentDialog failedLoadDialog = new ContentDialog
                    {
                        Title = resourceLoader.GetString("XmlFailedTitle"),
                        Content = msg,
                        CloseButtonText = resourceLoader.GetString("Ok")
                    };

                    _ = await failedLoadDialog.ShowAsync();
                }
            }
        }

        private string lastNavTag;
        private readonly SemaphoreSlim connectionSem;
        private readonly ApplicationDataContainer localSettings;
        private readonly ResourceLoader resourceLoader = ResourceLoader.GetForCurrentView();

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    connectionSem?.Dispose();
                }

                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

    }
}
