using System;
using System.ComponentModel;
using System.Windows;
using SmartBinApp.ViewModels;

namespace SmartBinApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml.
    /// Main control window for the AI Smart Bin station.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            Closing += OnMainWindowClosing;
        }

        /// <summary>
        /// Handles window closing event to gracefully release hardware and background tasks.
        /// </summary>
        private void OnMainWindowClosing(object? sender, CancelEventArgs e)
        {
            try
            {
                _viewModel.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing window resources: {ex.Message}");
            }
        }
    }
}