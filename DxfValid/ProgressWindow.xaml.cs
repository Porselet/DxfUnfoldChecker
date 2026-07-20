using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using System.ComponentModel;


namespace DxfValid
{
    public partial class ProgressWindow : Window
    {
        // Наш предохранитель: по умолчанию закрывать окно НЕЛЬЗЯ
        public bool IsScanFinished { get; set; } = false;

        public ProgressWindow()
        {
            InitializeComponent();
        }

        // Перехватываем попытку закрыть окно (например, клик по крестику)
        protected override void OnClosing(CancelEventArgs e)
        {
            // Если скан еще НЕ закончен — отменяем закрытие окна!
            if (!IsScanFinished)
            {
                e.Cancel = true;
            }
            else
            {
                base.OnClosing(e);
            }
        }
    }
}

