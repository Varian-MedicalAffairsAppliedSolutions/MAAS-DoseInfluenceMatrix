//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Runtime.Remoting.Contexts;
//using System.Text;
//using System.Threading.Tasks;
//using System.Windows;
//using System.Windows.Controls;
//using System.Windows.Data;
//using System.Windows.Documents;
//using System.Windows.Input;
//using System.Windows.Media;
//using System.Windows.Media.Imaging;
//using System.Windows.Navigation;
//using System.Windows.Shapes;
//using System.Windows.Threading;

//namespace PhotonInfluenceMatrixCalc
//{
//    /// <summary>
//    /// Interaction logic for UserControl1.xaml
//    /// </summary>
//    public partial class ctrlMain : UserControl
//    {
//        VMS.TPS.Script m_hScript;
//        System.Windows.Window m_hMainWnd;

//        public ctrlMain()
//        {
//            InitializeComponent();
//        }
//        public ctrlMain(VMS.TPS.Script script, System.Windows.Window hMainWnd)
//        {
//            m_hScript = script;
//            InitializeComponent();
//            m_hMainWnd = hMainWnd;
//        }

//        private void butCalculate_Click(object sender, RoutedEventArgs e)
//        {
//            butClose.IsEnabled = false;
//            butCalculate.IsEnabled = false;

//            m_hScript.RunInfMatrixCalc();

//            butClose.IsEnabled = true;
//            butCalculate.IsEnabled = true;
//        }
//        public void AddMessage(string szMsg)
//        {
//            txtMessages.Text = txtMessages.Text + "\n" + szMsg;
//            txtMessages.ScrollToEnd();
//            Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle, null);
//        }

//        private void butClose_Click(object sender, RoutedEventArgs e)
//        {
//            m_hMainWnd.Close();
//        }
//    }
//}


using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace PhotonInfluenceMatrixCalc
{
    /// <summary>
    /// Interaction logic for ctrlMain.xaml
    /// </summary>
    public partial class ctrlMain : UserControl, INotifyPropertyChanged
    {
        VMS.TPS.Script m_hScript;
        System.Windows.Window m_hMainWnd;

        private string _validationWarning = string.Empty;
        public string ValidationWarning
        {
            get { return _validationWarning; }
            set
            {
                _validationWarning = value;
                OnPropertyChanged("ValidationWarning");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }

        public ctrlMain()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        public ctrlMain(VMS.TPS.Script script, System.Windows.Window hMainWnd)
        {
            m_hScript = script;
            m_hMainWnd = hMainWnd;

            InitializeComponent();
            this.DataContext = this;

            // Check validation status
            CheckValidationStatus();
        }

        private void CheckValidationStatus()
        {
            try
            {
                // Path to config file - adjust as needed
                string configPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "YourCompany", "PhotonInfluenceMatrix", "config.ini");

                bool validated = false;

                if (System.IO.File.Exists(configPath))
                {
                    // Read config file
                    string[] lines = System.IO.File.ReadAllLines(configPath);
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("Validated="))
                        {
                            validated = line.Equals("Validated=True", StringComparison.OrdinalIgnoreCase);
                            break;
                        }
                    }
                }

                // Set validation warning if not validated
                if (!validated)
                {
                    ValidationWarning = "***Not validated for Clinical Use***";
                }
            }
            catch (Exception)
            {
                // If there's an error reading config, default to not validated
                ValidationWarning = "***Not validated for Clinical Use***";
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch
            {
                MessageBox.Show("Could not open license URL.");
            }
            e.Handled = true;
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

            try
            {
                this.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle, null);
            }
            catch
            {
                // Ignore dispatcher errors
            }
        }

        private void butClose_Click(object sender, RoutedEventArgs e)
        {
            m_hMainWnd.Close();
        }
    }
}