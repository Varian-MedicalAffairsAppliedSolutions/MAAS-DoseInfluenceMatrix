﻿using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Serilog.Parsing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Diagnostics.Metrics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Remoting.Contexts;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;
using VMS.TPS.Common.Model.API;

namespace CalculateInfluenceMatrix
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class ctrlMain : UserControl, INotifyPropertyChanged
    {
        VMS.TPS.Script m_hScript;
        System.Windows.Window m_hMainWnd;
        private bool _isCalculating = false;
        private bool _cancelRequested = false;
        public static bool CancellationRequested { get; set; } = false;

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

            // Check EULA agreement
            bool eulaAgreed = CheckEulaAgreement();
            if (!eulaAgreed)
            {
                // Instead of trying to close the window, just set a message and disable buttons
                txtMessages.Text = "You must agree to the LUSLA terms to use this application.";
                butCalculate.IsEnabled = false;
                return;
            }

            // Check validation status
            CheckValidationStatus();
        }

        public ctrlMain(VMS.TPS.Script script, System.Windows.Window hMainWnd, string szTitle)
        {
            m_hScript = script;
            m_hMainWnd = hMainWnd;

            InitializeComponent();
            this.DataContext = this;

            // Check EULA agreement
            bool eulaAgreed = CheckEulaAgreement();
            if (!eulaAgreed)
            {
                // Instead of trying to close the window, just set a message and disable buttons
                txtMessages.Text = "You must agree to the LUSLA terms to use this application.";
                butCalculate.IsEnabled = false;
                return;
            }

            // Check for license expiration (NOEXPIRE flag)
            CheckLicenseExpiration();

            // Check validation status
            CheckValidationStatus();

            tbTitle.Text = szTitle;
        }

        private void CheckLicenseExpiration()
        {
            // Check for NOEXPIRE environment variable
            if (Environment.GetEnvironmentVariable("NOEXPIRE") == null)
            {
                // Handle license expiration logic here
                // For example, check license file, etc.
            }
        }

        private bool CheckEulaAgreement()
        {
            try
            {
                // Get the location of the executing assembly
                string assemblyLocation = Assembly.GetExecutingAssembly().Location;
                string configPath = assemblyLocation + ".config";

                if (File.Exists(configPath))
                {
                    var doc = XDocument.Load(configPath);
                    var eulaAgree = doc
                        .Descendants("configuration")
                        .Descendants("appSettings")
                        .Descendants("add")
                        .Where(x => x.Attribute("key")?.Value == "EULAAgree")
                        .Select(x => x.Attribute("value")?.Value)
                        .FirstOrDefault();

                    // If EULA agreement is already set to true, return true
                    if (!string.IsNullOrEmpty(eulaAgree) &&
                        eulaAgree.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                // Create a window to display the license text with a scrollable TextBox
                Window licenseWindow = new Window
                {
                    Title = "Varian LUSLA License Agreement",
                    Width = 700,
                    Height = 500,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.CanResize
                };

                // Create a grid layout
                Grid grid = new Grid();
                RowDefinition row1 = new RowDefinition { Height = new GridLength(1, GridUnitType.Star) };
                RowDefinition row2 = new RowDefinition { Height = GridLength.Auto };
                grid.RowDefinitions.Add(row1);
                grid.RowDefinitions.Add(row2);

                // Create a scrollable TextBox for the license text
                TextBox licenseTextBox = new TextBox
                {
                    Text = GetLicenseText(),
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(10)
                };
                Grid.SetRow(licenseTextBox, 0);

                // Create a panel for buttons
                StackPanel buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(10)
                };
                Grid.SetRow(buttonPanel, 1);

                // Create agree and disagree buttons
                Button agreeButton = new Button
                {
                    Content = "I Agree",
                    Padding = new Thickness(20, 5, 20, 5),
                    Margin = new Thickness(5)
                };
                Button disagreeButton = new Button
                {
                    Content = "I Do Not Agree",
                    Padding = new Thickness(20, 5, 20, 5),
                    Margin = new Thickness(5)
                };

                // Add buttons to panel
                buttonPanel.Children.Add(agreeButton);
                buttonPanel.Children.Add(disagreeButton);

                // Add controls to the grid
                grid.Children.Add(licenseTextBox);
                grid.Children.Add(buttonPanel);

                // Set the window content
                licenseWindow.Content = grid;

                // Set up button click handlers
                bool agreed = false;
                agreeButton.Click += (s, e) =>
                {
                    agreed = true;
                    licenseWindow.Close();
                };
                disagreeButton.Click += (s, e) =>
                {
                    agreed = false;
                    licenseWindow.Close();
                };

                // Show the window as dialog
                licenseWindow.ShowDialog();

                // Update the config file with the user's choice
                if (File.Exists(configPath))
                {
                    try
                    {
                        var doc = XDocument.Load(configPath);
                        var eulaElement = doc
                            .Descendants("configuration")
                            .Descendants("appSettings")
                            .Descendants("add")
                            .FirstOrDefault(x => x.Attribute("key")?.Value == "EULAAgree");

                        if (eulaElement != null)
                        {
                            // Update existing element
                            eulaElement.SetAttributeValue("value", agreed.ToString().ToLower());
                        }
                        else
                        {
                            // Add new element
                            var appSettings = doc.Descendants("configuration").Descendants("appSettings").FirstOrDefault();
                            if (appSettings != null)
                            {
                                appSettings.Add(new XElement("add",
                                    new XAttribute("key", "EULAAgree"),
                                    new XAttribute("value", agreed.ToString().ToLower())));
                            }
                        }

                        // Save the updated config
                        doc.Save(configPath);
                    }
                    catch
                    {
                        // Ignore errors updating config file
                    }
                }

                return agreed;
            }
            catch (Exception)
            {
                // If we fail, fall back to a simple MessageBox
                MessageBoxResult result = MessageBox.Show(
                    "An error occurred displaying the full license. Do you agree to the terms of the Varian LUSLA?",
                    "License Agreement",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                return (result == MessageBoxResult.Yes);
            }
        }

        private string GetLicenseText()
        {
            return @"VARIAN LICENSE AGREEMENT

        LIMITED USE SOFTWARE LICENSE AGREEMENT
        This Limited Use Software License Agreement (the ""Agreement"") is a legal agreement between you , the
        user (“You”), and Varian Medical Systems, Inc. (""Varian""). By downloading or otherwise accessing the
        software material, which includes source code (the ""Source Code"") and related software tools (collectively,
        the ""Software""), You are agreeing to be bound by the terms of this Agreement. If You are entering into this
        Agreement on behalf of an institution or company, You represent and warrant that You are authorized to do
        so. If You do not agree to the terms of this Agreement, You may not use the Software and must immediately
        destroy any Software You may have downloaded or copied.
        SOFTWARE LICENSE
        1. Grant of License. Varian grants to You a non-transferable, non-sublicensable license to use
        the Software solely as provided in Section 2 (Permitted Uses) below. Access to the Software will be
        facilitated through a source code repository provided by Varian.
        2. Permitted Uses. You may download, compile and use the Software, You may (but are not required to do
        so) suggest to Varian improvements or otherwise provide feedback to Varian with respect to the
        Software. You may modify the Software solely in support of such use, and You may upload such
        modified Software to Varian’s source code repository. Any derivation of the Software (including compiled
        binaries) must display prominently the terms and conditions of this Agreement in the interactive user
        interface, such that use of the Software cannot continue until the user has acknowledged having read
        this Agreement via click-through.
        3. Publications. Solely in connection with your use of the Software as permitted under this Agreement, You
        may make reference to this Software in connection with such use in academic research publications
        after notifying an authorized representative of Varian in writing in each instance. Notwithstanding the
        foregoing, You may not make reference to the Software in any way that may indicate or imply any
        approval or endorsement by Varian of the results of any use of the Software by You.
        4. Prohibited Uses. Under no circumstances are You permitted, allowed or authorized to distribute the
        Software or any modifications to the Software for any purpose, including, but not limited to, renting,
        selling, or leasing the Software or any modifications to the Software, for free or otherwise. You may not
        disclose the Software to any third party without the prior express written consent of an authorized
        representative of Varian. You may not reproduce, copy or disclose to others, in whole or in any part, the
        Software or modifications to the Software, except within Your own institution or company, as applicable,
        to facilitate Your permitted use of the Software. You agree that the Software will not be shipped,
        transferred or exported into any country in violation of the U.S. Export Administration Act (or any other
        law governing such matters) and that You will not utilize, in any other manner, the Software in
        violation of any applicable law.
        5. Intellectual Property Rights. All intellectual property rights in the Software and any modifications to the
        Software are owned solely and exclusively by Varian, and You shall have no ownership or other
        proprietary interest in the Software or any modifications. You hereby transfer and assign to Varian all
        right, title and interest in any such modifications to the Software that you may have made or contributed.
        You hereby waive any and all moral rights that you may have with respect to such modifications, and
        hereby waive any rights of attribution relating to any modifications of the Software. You acknowledge
        that Varian will have the sole right to commercialize and otherwise use, whether directly or through third
        parties, any modifications to the Software that you provide to Varian’s repository. Varian may make any
        use it determines to be appropriate with respect to any feedback, suggestions or other communications
        that You provide with respect to the Software or any modifications.
        6. No Support Obligations. Varian is under no obligation to provide any support or technical assistance in
        connection with the Software or any modifications. Any such support or technical assistance is entirely
        discretionary on the part of Varian, and may be discontinued at any time without liability.
        7. NO WARRANTIES. THE SOFTWARE AND ANY SUPPORT PROVIDED BY VARIAN ARE PROVIDED
        “AS IS” AND “WITH ALL FAULTS.” VARIAN DISCLAIMS ALL WARRANTIES, BOTH EXPRESS AND
        IMPLIED, INCLUDING BUT NOT LIMITED TO IMPLIED WARRANTIES OF MERCHANTABILITY,
        FITNESS FOR A PARTICULAR PURPOSE, AND NON-INFRINGEMENT WITH RESPECT TO THE
        SOFTWARE AND ANY SUPPORT. VARIAN DOES NOT WARRANT THAT THE OPERATION OF THE
        SOFTWARE WILL BE UNINTERRUPTED, ERROR FREE OR MEET YOUR SPECIFIC
        REQUIREMENTS OR INTENDED USE. THE AGENTS AND EMPLOYEES OF VARIAN ARE NOT
        AUTHORIZED TO MAKE MODIFICATIONS TO THIS PROVISION, OR PROVIDE ADDITIONAL
        WARRANTIES ON BEHALF OF VARIAN.
        8. No Regulatory Clearance. The Software is not cleared or approved for use by any regulatory body in any
        jurisdiction.
        9. Termination. You may terminate this Agreement, and the right to use the Software, at any time upon
        written notice to Varian. Varian may terminate this Agreement, and the right to use the Software, at any
        time upon notice to You in the event that Varian determines that you are not using the Software in
        accordance with this Agreement or have otherwise breached any provision of this Agreement. The
        Software, together with any modifications to it or any permitted archive copy thereof, shall be destroyed
        when no longer used in accordance with this Agreement, or when the right to use the Software is
        terminated.
        10. Limitation of Liability. IN NO EVENT SHALL VARIAN BE LIABLE FOR LOSS OF DATA, LOSS OF
        PROFITS, LOST SAVINGS, SPECIAL, INCIDENTAL, CONSEQUENTIAL, INDIRECT OR
        OTHER SIMILAR DAMAGES ARISING FROM BREACH OF WARRANTY, BREACH OF
        CONTRACT, NEGLIGENCE, OR OTHER LEGAL THEORY EVEN IF VARIAN OR ITS AGENT HAS
        BEEN ADVISED OF THE POSSIBILITY OF SUCH DAMAGES, OR FOR ANY CLAIM BY ANY OTHER
        PARTY.
        11. Indemnification. You will defend, indemnify and hold harmless Varian, its affiliates and their respective
        officers, directors, employees, sublicensees, contractors, users and agents from any and all claims,
        losses, liabilities, damages, expenses and costs (including attorneys’ fees and court costs) arising out of
        any third-party claims related to or arising from your use of the Software or any modifications to the
        Software.
        12. Assignment. You may not assign any of Your rights or obligations under this Agreement without the
        written consent of Varian.
        13. Governing Law. This Agreement will be governed and construed under the laws of the State of California
        and the United States of America without regard to conflicts of law provisions. The parties agree to the
        exclusive jurisdiction of the state and federal courts located in Santa Clara County, California with
        respect to any disputes under or relating to this Agreement.
        14. Entire Agreement. This Agreement is the entire agreement of the parties as to the subject matter and
        supersedes all prior written and oral agreements and understandings relating to same. The Agreement
        may only be modified or amended in a writing signed by the parties that makes specific reference to the
        Agreement and the provision the parties intend to modify or amend. 

        By clicking 'I Agree', you acknowledge that you have read, understood, and agree to be bound by the terms of the Varian LUSLA.";
        }


        private void CheckValidationStatus()
    {
        try
        {
            // Get the location of the executing assembly
            string assemblyLocation = Assembly.GetExecutingAssembly().Location;
            string configPath = assemblyLocation + ".config";

            // Default to showing the warning
            ValidationWarning = "***Not validated for Clinical Use***";

            if (File.Exists(configPath))
            {
                var doc = XDocument.Load(configPath);
                var validationSetting = doc
                    .Descendants("configuration")
                    .Descendants("appSettings")
                    .Descendants("add")
                    .Where(x => x.Attribute("key")?.Value == "Validation")
                    .Select(x => x.Attribute("value")?.Value)
                    .FirstOrDefault();

                // If validation is explicitly set to "true", clear the warning
                if (!string.IsNullOrEmpty(validationSetting) &&
                    validationSetting.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    ValidationWarning = string.Empty;
                }
            }
        }
        catch (Exception)
        {
            // Default to showing the warning on error
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
            // If already calculating, this acts as a cancel button
            if (_isCalculating)
            {
                _cancelRequested = true;
                CancellationRequested = true;
                butCalculate.Content = "Cancelling...";
                butCalculate.IsEnabled = false;
                AddMessage("Cancellation requested. Please wait...");

                // Force a more aggressive cancellation approach
                // This will periodically try to interrupt the calculation
                DispatcherTimer forceTimer = new DispatcherTimer();
                forceTimer.Interval = TimeSpan.FromSeconds(3); // Check every 3 seconds
                forceTimer.Tick += (s, args) =>
                {
                    if (!_isCalculating)
                    {
                        ((DispatcherTimer)s).Stop();
                        return;
                    }

                    // Try more aggressive cancellation
                    AddMessage("Still attempting to cancel...");
                    CancellationRequested = true;

                    // After a certain number of attempts, suggest closing the application
                    if (forceTimer.Tag == null)
                        forceTimer.Tag = 0;

                    int attempts = (int)forceTimer.Tag + 1;
                    forceTimer.Tag = attempts;

                    if (attempts > 3) // After about 9 seconds
                    {
                        AddMessage("Cancellation is taking longer than expected. You may need to close the application if it doesn't respond.");
                        forceTimer.Stop();
                    }
                };
                forceTimer.Start();

                return;
            }

            // Start calculation
            _isCalculating = true;
            _cancelRequested = false;
            CancellationRequested = false;

            // Change button to Cancel during calculation
            butCalculate.Content = "Cancel after current step";
            butClose.IsEnabled = false;

            // Create a timer to keep the UI responsive
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(100);
            timer.Tick += (s, args) =>
            {
                // Process pending UI events to keep the interface responsive
                this.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

                // Update the cancellation flag if requested
                if (_cancelRequested)
                {
                    CancellationRequested = true;
                }
            };
            timer.Start();

            try
            {
                // Run the calculation
                m_hScript.RunInfMatrixCalc();
            }
            catch (OperationCanceledException)
            {
                AddMessage("Calculation was cancelled.");
            }
            catch (Exception ex)
            {
                AddMessage("Error: " + ex.Message);
            }
            finally
            {
                // Stop the timer
                timer.Stop();

                // Reset UI state
                _isCalculating = false;
                _cancelRequested = false;
                CancellationRequested = false;

                this.Dispatcher.Invoke(() =>
                {
                    butCalculate.Content = "Calculate";
                    butCalculate.IsEnabled = true;
                    butClose.IsEnabled = true;
                });
            }
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
            // If a calculation is in progress, cancel it first
            if (_isCalculating)
            {
                _cancelRequested = true;
                AddMessage("Cancelling calculation before closing...");
                // Wait a bit for the cancellation to be processed
                System.Threading.Thread.Sleep(500);
            }

            m_hMainWnd.Close();
        }

    }
}