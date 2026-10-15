using System;
using System.ComponentModel;
using System.Windows;
using SmartBinApp.ViewModels;

namespace SmartBinApp
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml.
    /// Главное окно приложения управления станцией AI Smart Bin.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            // Создаем экземпляр главной ViewModel с инкапсулированными сервисами
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // Подписываемся на закрытие окна для корректного освобождения неуправляемых ресурсов (камера, COM-порт)
            Closing += OnMainWindowClosing;
        }

        /// <summary>
        /// Обработчик события закрытия окна.
        /// Гарантирует корректную остановку фонового потока захвата OpenCV и закрытие SerialPort.
        /// </summary>
        private void OnMainWindowClosing(object? sender, CancelEventArgs e)
        {
            try
            {
                _viewModel.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при закрытии ресурсов окна: {ex.Message}");
            }
        }
    }
}