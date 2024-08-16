using GS.SCard;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

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

        private string connectionString = "server=localhost;port=3306;database=employee_db;user=root;password=";

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
            string promptMessage = "";

            // Check if a company is selected
            if (cmbCompany.SelectedItem == null && string.IsNullOrEmpty(qrText))
            {
                txtPrompt.Text = "No changes made.";
                return;
            }

            // Check if a company is selected but no ID is provided
            if (cmbCompany.SelectedItem != null && string.IsNullOrEmpty(qrText))
            {
                txtPrompt.Text = "Please enter ID number.";
                return;
            }

            // Check for empty input
            if (string.IsNullOrEmpty(qrText))
            {
                txtPrompt.Text = "No changes made.";
                return;
            }

            // Check for insufficient characters
            if (qrText.Length < 7)
            {
                txtPrompt.Text = "Insufficient characters on ID.";
                return;
            }

            // Ensure a company is selected
            if (cmbCompany.SelectedItem == null)
            {
                txtPrompt.Text = "Please select a company.";
                return;
            }

            var selectedCompanyCode = (string)cmbCompany.SelectedItem;
            var companyInfo = companyData[selectedCompanyCode];
            var suffix = companyInfo.Suffix;

            // Combine user input with suffix to create the new full ID
            string fullID = qrText + suffix;

            // Check if the original user input exists in the database
            bool idExists = CheckIfIDExistsInDatabase(qrText);

            // Proceed with card writing logic if a reader is connected
            try
            {
                scard.EstablishContext();
                scard.ListReaders();

                if (scard.ReaderNames.Length == 0)
                {
                    txtPrompt.Text = "No reader connected.";
                    return;
                }
            }
            catch (Exception)
            {
                txtPrompt.Text = "No reader connected.";
                return;
            }

            txtPrompt.Text = "Put the card.";
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background); // Ensure UI update

            try
            {
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
                    // Display success message for card writing
                    promptMessage += "Card ID updated successfully." + Environment.NewLine;

                    // Check if the database update is needed
                    if (idExists)
                    {
                        // Update the database with the new ID
                        UpdateIDInDatabase(qrText, fullID); // id_data will be set to fullID
                        promptMessage += "Database updated successfully.";
                    }
                    else
                    {
                        promptMessage += "ID Number can't be found in the database.";
                    }

                    // Wait for card removal
                    while (scard.GetCardPresentState(readerName))
                    {
                        await Task.Delay(100); // Add a short delay to prevent tight loop
                    }

                    // Refresh the message after the card is removed
                    Dispatcher.Invoke(() =>
                    {
                        txtPrompt.Text = promptMessage;
                        lblNewID.Content = qrText; // Display new ID without suffix

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


        private bool CheckIfIDExistsInDatabase(string originalID)
        {
            using (MySqlConnection conn = new MySqlConnection(connectionString))
            {
                try
                {
                    conn.Open();

                    string query = "SELECT COUNT(*) FROM tk_data WHERE id_number = @OriginalID";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@OriginalID", originalID);

                        int count = Convert.ToInt32(cmd.ExecuteScalar());
                        return count > 0;
                    }
                }
                catch (MySqlException ex)
                {
                    MessageBox.Show($"Database error: {ex.Message}");
                    return false;
                }
            }
        }

        private void UpdateIDInDatabase(string originalID, string newFullID)
        {
            var selectedCompanyCode = (string)cmbCompany.SelectedItem;
            if (!companyData.TryGetValue(selectedCompanyCode, out var companyInfo))
            {
                MessageBox.Show("Company information not found.");
                return;
            }

            string companyId = companyInfo.Suffix; // Assuming companyInfo.Suffix contains company_id

            using (MySqlConnection conn = new MySqlConnection(connectionString))
            {
                try
                {
                    conn.Open();

                    // Update id_data to be the same as id_number, but only for the matching company_id
                    string query = "UPDATE tk_data SET id_data = id_number WHERE id_number = @OriginalID AND company_id = @CompanyID";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@OriginalID", originalID); // id_number to find the correct record
                        cmd.Parameters.AddWithValue("@CompanyID", companyId); // company_id to specify the correct company

                        int rowsAffected = cmd.ExecuteNonQuery();
                        if (rowsAffected == 0)
                        {
                            MessageBox.Show("No matching records found for the specified company.");
                        }
                    }
                }
                catch (MySqlException ex)
                {
                    MessageBox.Show($"Database error: {ex.Message}");
                }
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