using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using VMS.TPS.Common.Model.API;

namespace CalculateInfluenceMatrix
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class ctrlMain : UserControl
    {
        VMS.TPS.Script m_hScript;
        System.Windows.Window m_hMainWnd;

        public ctrlMain()
        {
            InitializeComponent();
        }
        public ctrlMain(VMS.TPS.Script script, System.Windows.Window hMainWnd)
        {
            m_hScript = script;
            InitializeComponent();
            m_hMainWnd = hMainWnd;
        }

        private void butCalculate_Click(object sender, RoutedEventArgs e)
        {
            butClose.IsEnabled = false;
            butCalculate.IsEnabled = false;

            m_hScript.RunInfMatrixCalc();

            butClose.IsEnabled = true;
            butCalculate.IsEnabled = true;
        }
        public void AddMessage(string szMsg)
        {
            txtMessages.Text = txtMessages.Text + "\n" + szMsg;
            txtMessages.ScrollToEnd();
            Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle, null);
        }

        private void butClose_Click(object sender, RoutedEventArgs e)
        {
            m_hMainWnd.Close();
        }
    }
}
