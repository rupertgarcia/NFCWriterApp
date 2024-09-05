using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading;
using GS.SCard;
using System.Globalization;
using System.Linq;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;

namespace RFIDReaderApp
{
    public partial class MainWindow : Window
    {
        private string folderPath = Path.Combine("C:", "NFCWriter");
        private WinSCard scard = new WinSCard();
        private string currentCardId = string.Empty;
        private Task monitoringTask;
        private CancellationTokenSource cancellationTokenSource;

        public class CompanyInfo
        {
            public string Description { get; set; }
            public string Suffix { get; set; }
        }

        public const string BeginningMessage = "Get new ID card and scan QR Code. Make sure reader has no card.\n" +
                                        "1. Input New ID and Select Company\n" +
                                        "2. Place Card on Reader\n" +
                                        "3. Press Save\n" +
                                        "4. Wait for Beep and Take Off Card from Reader\n" +
                                        "5. Press Clear to Start Again";

        private string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IDChangeLog.txt");

        private Dictionary<string, CompanyInfo> companyData = new Dictionary<string, CompanyInfo>
        {
            { "AMI", new CompanyInfo { Description = "Alliance Mansols Inc.", Suffix = "01" } },
            { "EMSCAI", new CompanyInfo { Description = "EMS Components Assembly Inc.", Suffix = "03" } },
            { "ERTI", new CompanyInfo { Description = "EMS Resources Technology Inc.", Suffix = "05" } },
            { "CREO", new CompanyInfo { Description = "Creotec Philippines Inc.", Suffix = "17" } },
            { "ESPI", new CompanyInfo { Description = "EMS Services Philippines Inc.", Suffix = "18" } },
            { "ESII", new CompanyInfo { Description = "EMS Services International Inc.", Suffix = "19" } },
            { "GRUPPO", new CompanyInfo { Description = "GRUPPO EMS", Suffix = "20" } },
            { "DUALTECH", new CompanyInfo { Description = "Dualtech Training Center", Suffix = "21" } },
        };

        public MainWindow()
        {
            InitializeComponent();
            PopulateComboBox();
            txtPrompt.Text = BeginningMessage;
            txtQR.Focus();

            // Add event handler for View Logs button
            View_Logs.Click += ViewLogs_Click;

            // Ensure the directory exists when the application starts
            EnsureDirectoryExists();

            StartMonitoringCard();
        }

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
        }            // Ensure the directory exists when the application starts

        private void PopulateComboBox()
        {
            cmbCompany.ItemsSource = companyData.Keys;
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbCompany.SelectedItem is string selectedCompanyCode)
            {
                if (companyData.TryGetValue(selectedCompanyCode, out var companyInfo))
                {
                    lblCompanyDescription.Content = companyInfo.Description;
                }
            }
        }

        private void txtQR_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                SaveID();
            }
        }

        private void StartMonitoringCard()
        {
            cancellationTokenSource = new CancellationTokenSource();
            monitoringTask = Task.Run(() => MonitorCard(cancellationTokenSource.Token), cancellationTokenSource.Token);
        }

        private async Task MonitorCard(CancellationToken token)
        {
            try
            {
                scard.EstablishContext();
                scard.ListReaders();

                if (scard.ReaderNames.Length == 0)
                {
                    Dispatcher.Invoke(() => lblCurrentID.Content = "No card reader found.");
                    return;
                }

                string readerName = scard.ReaderNames[0];

                while (!token.IsCancellationRequested)
                {
                    bool cardPresent = scard.GetCardPresentState(readerName);

                    if (cardPresent)
                    {
                        string id = await ReadCardIdAsync();
                        if (id != currentCardId)
                        {
                            currentCardId = id;
                            Dispatcher.Invoke(() =>
                            {
                                lblCurrentID.Content = string.IsNullOrEmpty(currentCardId) ? "0000000" : currentCardId;
                            });
                        }
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            lblCurrentID.Content = string.Empty; // Clear the label when no card is detected
                        });
                        currentCardId = string.Empty;
                    }

                    await Task.Delay(500); // Delay to prevent tight loop
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => txtPrompt.Text = $"Error monitoring card: {ex.Message}");
            }
            finally
            {
                scard.Disconnect();
                scard.ReleaseContext();
            }
        }



        private async Task<string> ReadCardIdAsync()
        {
            await Task.Delay(100);

            string result = string.Empty;
            try
            {
                scard.EstablishContext();
                scard.ListReaders();

                if (scard.ReaderNames.Length == 0)
                {
                    Dispatcher.Invoke(() => txtPrompt.Text = "No card reader found.");
                    return result;
                }

                byte[] cmdApduAuth = { 0xFF, 0x86, 0x00, 0x00, 0x05, 0x01, 0x00, 0x01, 0x60, 0x00 }; // authenticate
                byte[] respApduAuth = new byte[256];
                int respLengthAuth = respApduAuth.Length;

                byte[] cmdApdu = { 0xFF, 0xB0, 0x00, 0x01, 0x10 }; // read section 0 block 1
                byte[] respApdu = new byte[256];
                int respLength = respApdu.Length;

                string readerName = scard.ReaderNames[0];

                scard.Connect(readerName);
                scard.Transmit(cmdApduAuth, cmdApduAuth.Length, respApduAuth, ref respLengthAuth);
                scard.Transmit(cmdApdu, cmdApdu.Length, respApdu, ref respLength);

                result = BitConverter.ToString(respApdu.Take(5).ToArray()).Replace("-", "");
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => txtPrompt.Text = $"Error reading card ID: {ex.Message}");
            }
            finally
            {
                scard.Disconnect();
                scard.ReleaseContext();
            }

            return result;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            cancellationTokenSource?.Cancel();
            monitoringTask?.Wait();
        }




        private async void SaveID()
        {
            string oldId = await ReadCardIdAsync(); // Get the old ID
            string qrText = txtQR.Text;

            // Check for empty input
            if (string.IsNullOrEmpty(qrText))
            {
                txtPrompt.Text = "No changes made.";
                return;
            }

            // Check for insufficient characters
            if (qrText.Length < 7) // Adjust the length check based on your requirements
            {
                txtPrompt.Text = "Insufficient characters on ID.";
                return;
            }

            // Check if a company is selected
            if (cmbCompany.SelectedItem == null)
            {
                txtPrompt.Text = "Please select a company.";
                return;
            }

            var selectedCompanyCode = (string)cmbCompany.SelectedItem;
            var companyInfo = companyData[selectedCompanyCode];
            var suffix = companyInfo.Suffix;

            txtPrompt.Text = "Put the card.";
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background); // Ensure UI update

            try
            {
                scard.EstablishContext();
                scard.ListReaders();
                if (scard.ReaderNames.Length == 0)
                {
                    txtPrompt.Text = "No card reader found.";
                    return;
                }

                string readerName = scard.ReaderNames[0];

                // Wait for card presence with a delay to prevent a tight loop
                while (!scard.GetCardPresentState(readerName))
                {
                    await Task.Delay(200); // Add a short delay
                    txtPrompt.Text = "Put the card.";
                }

                // Card detected, proceed with connection
                scard.Connect(readerName);

                // Proceed with authentication and writing logic
                byte[] cmdApduAuth = { 0xFF, 0x86, 0x00, 0x00, 0x05, 0x01, 0x00, 0x01, 0x60, 0x00 }; // authenticate
                byte[] respApduAuth = new byte[256];
                int respLengthAuth = respApduAuth.Length;
                scard.Transmit(cmdApduAuth, cmdApduAuth.Length, respApduAuth, ref respLengthAuth);

                string hex0 = "0" + qrText.Substring(0, 1);
                string hex1 = qrText.Substring(1, 2);
                string hex2 = qrText.Substring(3, 2);
                string hex3 = qrText.Substring(5, 2);

                byte[] cmdApdu =
                {
            0xFF, 0xD6, 0x00, 0x01, 0x10,
            Byte.Parse(hex0, NumberStyles.HexNumber),
            Byte.Parse(hex1, NumberStyles.HexNumber),
            Byte.Parse(hex2, NumberStyles.HexNumber),
            Byte.Parse(hex3, NumberStyles.HexNumber),
            Byte.Parse(suffix, NumberStyles.HexNumber)
        };

                byte[] respApdu = new byte[256];
                int respLength = respApdu.Length;
                scard.Transmit(cmdApdu, cmdApdu.Length, respApdu, ref respLength);

                string resp = BitConverter.ToString(respApdu.Take(respLength).ToArray()).Replace("-", "");
                Console.WriteLine($"APDU Response: {resp}");

                if (resp.StartsWith("90"))
                {
                    // Successfully written, now read the new ID to verify
                    string newId = await ReadCardIdAsync();

                    // Display success message
                    txtPrompt.Text = "Success. Please remove the card.";
                    lblNewID.Content = newId; // Update the label with the new ID

                    // Log the changes including the old and new IDs
                    LogIDChange(oldId, newId, companyInfo.Description);

                    // Wait for card removal
                    while (scard.GetCardPresentState(readerName))
                    {
                        await Task.Delay(200); // Add a short delay to prevent tight loop
                    }

                    // Refresh the message after the card is removed
                    Dispatcher.Invoke(() =>
                    {
                        txtPrompt.Text = "Please scan a new card.";
                        txtQR.Text = "";
                        txtQR.Focus();
                    }, DispatcherPriority.ContextIdle);
                }
                else
                {
                    txtPrompt.Text = $"Failed. Response Code: {resp}";
                }
            }
            catch (Exception ex)
            {
                txtPrompt.Text = $"Error: {ex.Message}";
            }
            finally
            {
                // Ensure proper disconnection and context release
                scard.Disconnect();
                scard.ReleaseContext();
            }
        }



        private void LogIDChange(string oldID, string newID, string companyName)
        {
            // Define the directory and file path
            string folderPath = @"C:\NFCWriter";
            string logFilePath = Path.Combine(folderPath, "OldNewIDLogs.txt");

            // Check if the directory exists, if not, create it
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            // Construct the log entry
            string timestamp = DateTime.Now.ToString("MM/dd/yyyy : hh:mm tt", CultureInfo.InvariantCulture);
            string newLog = $"Old ID: {oldID}\nNew ID: {newID}\nCompany: {companyName}\nDate and Time: {timestamp}\n\n";

            // Write to the log file
            File.AppendAllText(logFilePath, newLog);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveID();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            // Clear the ID TextBox
            txtQR.Text = string.Empty;

            // Reset the ComboBox selection to no selected item
            cmbCompany.SelectedIndex = -1;

            // Reset the prompt message to the original message
            txtPrompt.Text = BeginningMessage;

            // Reset the company description label
            lblCompanyDescription.Content = "Company Description";

            // Clear the labels for new and current IDs
            lblNewID.Content = String.Empty;
            lblCurrentID.Content = String.Empty;

            // Set focus back to the txtQR TextBox for easy input
            txtQR.Focus();

            // Restart the card monitoring process
            RestartMonitoring();
        }

        private void RestartMonitoring()
        {
            // Cancel the existing monitoring task if it exists
            cancellationTokenSource?.Cancel();
            monitoringTask?.Wait();

            // Ensure that the monitoring task is properly disposed
            cancellationTokenSource?.Dispose();

            // Start a new monitoring task
            cancellationTokenSource = new CancellationTokenSource();
            monitoringTask = Task.Run(() => MonitorCard(cancellationTokenSource.Token), cancellationTokenSource.Token);
        }



        private void ViewLogs_Click(object sender, RoutedEventArgs e)
        {
            string logFilePath = @"C:\NFCWriter\OldNewIDLogs.txt";

            // Check if the log file exists, and if it does, open it in Notepad
            if (File.Exists(logFilePath))
            {
                Process.Start("notepad.exe", logFilePath);
            }
            else
            {
                txtPrompt.Text = "Log file not found.";
            }
        }
    }
}
