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

namespace RFIDReaderApp
{
    public partial class MainWindow : Window
    {
        public class CompanyInfo
        {
            public string Description { get; set; }
            public string Suffix { get; set; }
        }

        private WinSCard scard = new WinSCard();

        private Dictionary<string, CompanyInfo> companyData = new Dictionary<string, CompanyInfo>
        {
            { "AMI", new CompanyInfo { Description = "Alliance Mansols Inc.", Suffix = "1" } },
            { "EMSCAI", new CompanyInfo { Description = "EMS Components Assembly Inc.", Suffix = "3" } },
            { "ERTI", new CompanyInfo { Description = "EMS Resources Technology Inc.", Suffix = "5" } },
            { "CREO", new CompanyInfo { Description = "Creotec Philippines Inc.", Suffix = "17" } },
            { "ESPI", new CompanyInfo { Description = "EMS Services Philippines Inc.", Suffix = "18" } },
            { "ESII", new CompanyInfo { Description = "EMS Services International Inc.", Suffix = "19" } },
            { "GRUPPO", new CompanyInfo { Description = "GRUPPO EMS", Suffix = "20" } }
        };

        public MainWindow()
        {
            InitializeComponent();
            PopulateComboBox();
            txtPrompt.Text = "Get new ID card and scan QR Code. Make sure reader has no card.";
            txtQR.Focus();
        }

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

        private async void SaveID()
        {
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
                string readerName = scard.ReaderNames[0];

                // Wait for card presence
                while (!scard.GetCardPresentState(readerName))
                {
                    await Task.Delay(100); // Add a short delay to prevent tight loop
                    txtPrompt.Text = "Put the card.";
                }

                // Card detected, proceed with connection
                scard.Connect(readerName);

                string hex0 = "0" + qrText.Substring(0, 1);
                string hex1 = qrText.Substring(1, 2);
                string hex2 = qrText.Substring(3, 2);
                string hex3 = qrText.Substring(5, 2);

                byte[] cmdApduAuth =
                {
            0xFF, 0x86, 0x00, 0x00, 0x05,
            0x01, 0x00, 0x01, 0x60, 0x00
        }; // authenticate

                byte[] respApduAuth = new byte[256];
                int respLengthAuth = respApduAuth.Length;
                scard.Transmit(cmdApduAuth, cmdApduAuth.Length, respApduAuth, ref respLengthAuth);

                byte[] cmdApdu =
     {
    0xFF, 0xD6, 0x00, 0x01, 0x10,
    Byte.Parse(hex0, NumberStyles.HexNumber),
    Byte.Parse(hex1, NumberStyles.HexNumber),
    Byte.Parse(hex2, NumberStyles.HexNumber),
    Byte.Parse(hex3, NumberStyles.HexNumber),
    Byte.Parse(suffix, NumberStyles.HexNumber) // Use suffix from ComboBox
};


                byte[] respApdu = new byte[256];
                int respLength = respApdu.Length;
                scard.Transmit(cmdApdu, cmdApdu.Length, respApdu, ref respLength);

                string resp = BitConverter.ToString(respApdu.Take(respLength).ToArray()).Replace("-", "");
                Console.WriteLine($"APDU Response: {resp}");

                if (resp.StartsWith("90"))
                {
                    // Display success message
                    txtPrompt.Text = "Success. Please remove the card.";
                    lblNewID.Content = qrText; // Update the label with the new ID

                    // Wait for card removal
                    while (scard.GetCardPresentState(readerName))
                    {
                        await Task.Delay(100); // Add a short delay to prevent tight loop
                    }

                    // Refresh the message after the card is removed
                    Dispatcher.Invoke(() =>
                    {
                        txtPrompt.Text = "Get new ID card and scan QR Code. Make sure reader has no card.";
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
                MessageBox.Show($"Error: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                scard.Disconnect();
                scard.ReleaseContext();
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveID();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close(); // Closes the main window
        }
    }
}
