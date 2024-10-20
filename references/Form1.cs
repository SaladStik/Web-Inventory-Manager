using Inventory;
using Inventory.DB_Interaction;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;
using System.Reflection.Emit;
using System.Reflection;


namespace Inventory_Manager
{

    public partial class Form1 : Form
    {
        private DB_Integrator _dbIntegrator;
        private System.Windows.Forms.Timer _refreshTimer;
        private System.Windows.Forms.Timer _blinkTimer;
        private List<DataGridViewRow> _blinkingRows;
        private static string _JSESSIONID = "";
        private static string _AUTH = "";
        private string _tempDirectory;
        private Bitmap _barcodeBitmap;
        private string _labelConfigFile = "labelConfig.json"; // File to save label configurations
        public LabelConfig _labelConfig; // Label configuration object
        private string _currentSearchTerm;
        private int _currentScrollRowIndex;
        private int _horizontalScrollPosition;
        private ContextMenuStrip _contextMenu;

        public Form1()
        {
            InitializeComponent();
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.UserPaint, true);
            UpdateStyles();
            SetControlVisibilityForUserRole();
            InitializeContextMenu();
            _dbIntegrator = new DB_Integrator();
            Product_List.CellMouseDown += Product_List_CellMouseDown;

            // Initialize the refresh timer
            _refreshTimer = new System.Windows.Forms.Timer();
            _refreshTimer.Interval = 10000; // 10 seconds
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();

            _tempDirectory = Path.Combine(Path.GetTempPath(), "Inventory_BarcodeImages");
            Directory.CreateDirectory(_tempDirectory);

            // Initialize the blink timer
            _blinkTimer = new System.Windows.Forms.Timer();
            _blinkTimer.Interval = 2000; // 2 seconds
            _blinkTimer.Tick += BlinkTimer_Tick;
            _blinkTimer.Start();

            _blinkingRows = new List<DataGridViewRow>();
            // Load label configurations
            LoadLabelConfig();

            // Clean up temporary directory on application exit
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }
        private void InitializeContextMenu()
        {
            _contextMenu = new ContextMenuStrip();
            var openLogMenuItem = new ToolStripMenuItem("Open Log");
            openLogMenuItem.Click += OpenLogMenuItem_Click;
            _contextMenu.Items.Add(openLogMenuItem);
        }
        private void HideAdminTab()
        {
            searchTabs.TabPages.Remove(AdminTab);
        }


        private void SetControlVisibilityForUserRole()
        {
            bool isAdmin = UserSession.Role == "Administrator";
            bool isUser = UserSession.Role == "User";
            bool isViewer = UserSession.Role == "Viewer";

            // Hide the admin tab if the user isn't an admin
            if (UserSession.Role != "Administrator")
            {
                HideAdminTab();
            }

            // If someone somehow gets in, they won't be able to make changes
            if (!isAdmin && !isUser) { isViewer = true; }

            // Hide or disable buttons and controls for users
            quantityUp.Visible = isUser || isAdmin;
            quantityDown.Visible = isUser || isAdmin;
            QuantityChangeAmtBox.Visible = isUser || isAdmin;
            QuantityText.Visible = isUser || isAdmin;
            selectViaScan.Visible = isUser || isAdmin;

            if (isViewer)
            {
                searchTabs.TabPages.Remove(ProductUpdateTab);
                searchTabs.TabPages.Remove(ProdUpdates2);
                searchTabs.TabPages.Remove(tabPage3);
                Product_List.ReadOnly = true;
            }
            else
            {
                Product_List.ReadOnly = false;
            }
        }


        private void EnableDoubleBuffering()
        {
            typeof(DataGridView).InvokeMember("DoubleBuffered",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
                null, Product_List, new object[] { true });
        }


        public static string associate_product_supplier = "UPDATE product SET supplier_id = {0} WHERE id = {1};";
        public static string update_minimum_stock = "UPDATE product SET min_stock = {0} WHERE id = {1};";
        public static string select_Product = "SELECT p.id, p.model_number, p.alias, p.type, p.quantity, p.barcode, p.require_serial_number, p.image_url, s.name AS supplier, p.supplier_link, p.min_stock, p.bin FROM product p LEFT JOIN supplier s ON p.supplier_id = s.id ORDER BY p.id ASC";
        public static string product_add = @"
            INSERT INTO product (
                model_number, alias, type, quantity, barcode, require_serial_number, supplier_id, supplier_link, min_stock, bin
            ) VALUES (
                '{0}', '{1}', '{2}', {3}, '{4}', {5}, {6}, '{7}', {8}, '{9}'
            ) RETURNING id;";
        public static string product_update = "UPDATE product SET quantity = {0} WHERE barcode = '{1}'";
        public static string select_Location = "SELECT id FROM location WHERE name = '{0}';";
        public static string update_history_location = "UPDATE history SET id_location = '{0}' WHERE serial_number = '{1}' AND id_product = {2}";
        public static string insert_data_into_history_if_not_exists = @"
    INSERT INTO history (id_product, id_location, serial_number, date, note)
    SELECT {0}, {1}, '{2}', '{3}', '{4}'
    WHERE NOT EXISTS (SELECT * FROM history WHERE serial_number = '{2}')";

        public static string search_function = @"
            SELECT p.id, p.model_number, p.alias, p.type, p.quantity, p.barcode, p.require_serial_number, p.image_url, s.name AS supplier, p.supplier_link, p.min_stock, p.bin 
            FROM product p
            LEFT JOIN supplier s ON p.supplier_id = s.id
            WHERE CAST(p.id AS TEXT) ILIKE '%{0}%' 
            OR p.model_number ILIKE '%{0}%' 
            OR p.type ILIKE '%{0}%' 
            OR p.barcode ILIKE '%{0}%' 
            OR p.alias ILIKE '%{0}%'
            OR p.bin ILIKE '%{0}%';";
        public static string select_All_From_History = @"
             SELECT h.id, h.id_product, l.name AS location_name, h.serial_number, h.date, h.note, h.ticket_num
             FROM history h
             JOIN location l ON h.id_location = l.id
             WHERE h.id_product = {0}
             ORDER BY h.id ASC";
        public static string updateQuantity = @"
            UPDATE product
            SET quantity = quantity + {0}
            WHERE id = {1}";
        public static string update_history_note = "UPDATE history SET note = '{0}', id_location = {3} WHERE serial_number = '{1}' AND id_product = {2}";
        public static string insert_location = "INSERT INTO location(name) VALUES('{0}')";
        public static string insert_supplier = "INSERT INTO supplier(name) VALUES('{0}') RETURNING id;";
        public static string select_Supplier = "SELECT id, name FROM supplier ORDER BY name ASC;";
        public static string update_supplier_link = "UPDATE product SET supplier_link = '{0}' WHERE id = {1};";
        public static string update_bin = "UPDATE product SET bin = '{0}' WHERE id = {1};";
        public static string update_require_serial_number = "UPDATE product SET require_serial_number = {0} WHERE id = {1};";
        public static string insert_log = @"
    INSERT INTO the_log (event_id, users_id, product_id, date, previous_value, new_value, field_updated)
    VALUES ('{0}', {1}, {2}, '{3}', '{4}', '{5}', '{6}')
";



        private async void Form1_Load(object sender, EventArgs e)
        {
            // Unsubscribe from existing event handlers if they are already attached
            Product_List.CellDoubleClick -= Product_List_CellDoubleClick;
            Product_List.DataBindingComplete -= Product_List_DataBindingComplete;
            Product_List.SelectionChanged -= Product_List_SelectionChanged;
            ImageUploadButton.Click -= ImageUploadButton_Click;
            SupplierAddButton.Click -= SupplierAddButton_Click;
            SupplierLinkButton.Click -= SupplierLinkButton_Click;
            AddMinimumStock.Click -= AddMinimumStock_Click;
            MinimumStockTextBox.KeyPress -= MinimumStockTextBox_KeyPress;
            MinimumStockTextBox.TextChanged -= MinimumStockTextBox_TextChanged;
            BinSet.Click -= BinSet_Click;
            LocationNamePrintBox.Click -= LocationNamePrintBox_Click;
            UpdateType.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            UpdateType.AutoCompleteSource = AutoCompleteSource.ListItems;

            // Ensure the buttons have their event handlers attached
            setAlias.Click += setAlias_Click;
            setModelNum.Click += setModelNum_Click;
            setBarcode.Click += setBarcode_Click;
            setType.Click += setType_Click;
            await LoadTypesAsync(); // Load types into ComboBox

            await LoadTypesIntoUpdateTypeComboBoxAsync(); // Load types into UpdateType ComboBox
            // Subscribe to event handlers
            Product_List.CellDoubleClick += Product_List_CellDoubleClick;
            Product_List.DataBindingComplete += Product_List_DataBindingComplete;
            Product_List.SelectionChanged += Product_List_SelectionChanged;
            ImageUploadButton.Click += ImageUploadButton_Click;
            SupplierAddButton.Click += SupplierAddButton_Click; // Add Supplier Button Click
            SupplierLinkButton.Click += SupplierLinkButton_Click; // Supplier Link Button Click
            AddMinimumStock.Click += AddMinimumStock_Click; // Add Minimum Stock Button Click
            MinimumStockTextBox.KeyPress += MinimumStockTextBox_KeyPress; // Restrict to integers
            MinimumStockTextBox.TextChanged += MinimumStockTextBox_TextChanged; // Handle text changes
            BinSet.Click += BinSet_Click; // Add Bin Set Button Click handler
            LocationNamePrintBox.Click += LocationNamePrintBox_Click; // Add Location Name Print Button Click handler

            await LoadDataGridAsync();
            await LoadTypesAsync(); // Load types into ComboBox
            await LoadSuppliersAsync(); // Load suppliers into ComboBox
            await LoadLocationsIntoComboBoxAsync(); // Load locations into ComboBox


        }

        private async Task LoadLocationsIntoComboBoxAsync()
        {
            string query = "SELECT name FROM location ORDER BY name ASC";
            DataTable dataTable = await _dbIntegrator.GetDataTableAsync(query, null);

            locationNamePrintCombo.Items.Clear();
            foreach (DataRow row in dataTable.Rows)
            {
                locationNamePrintCombo.Items.Add(row["name"].ToString());
            }

            locationNamePrintCombo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            locationNamePrintCombo.AutoCompleteSource = AutoCompleteSource.ListItems;
        }

        private void LocationNamePrintBox_Click(object sender, EventArgs e)
        {
            // Get the selected location name
            string locationName = locationNamePrintCombo.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(locationName))
            {
                MessageBox.Show("Please select a location.");
                return;
            }

            // Show print dialog
            PrintDocument printDoc = new PrintDocument();
            printDoc.PrintPage += (s, ev) => PrintLocationName(ev, locationName);
            PrintDialog printDialog = new PrintDialog();
            printDialog.Document = printDoc;
            if (printDialog.ShowDialog() == DialogResult.OK)
            {
                printDoc.Print();
            }
        }

        private void MinimumStockTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            // Allow control keys such as backspace
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                e.Handled = true; // Ignore the key press
            }
        }

        private void MinimumStockTextBox_TextChanged(object sender, EventArgs e)
        {
            if (int.TryParse(MinimumStockTextBox.Text, out int result))
            {
                // Valid integer
            }
            else
            {
                // Handle invalid integer input if needed
            }
        }

        private async void AddMinimumStock_Click(object sender, EventArgs e)
        {
            if (Product_List.SelectedRows.Count > 0)
            {
                int productId = Convert.ToInt32(Product_List.SelectedRows[0].Cells["id"].Value);

                if (!int.TryParse(MinimumStockTextBox.Text.Trim(), out int minStockValue) || minStockValue < 0)
                {
                    MessageBox.Show("Please enter a valid minimum stock value.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                try
                {
                    // Fetch the current minimum stock value from the database
                    string currentMinStockQuery = $"SELECT min_stock FROM product WHERE id = {productId}";
                    object result = await _dbIntegrator.SelectAsync(currentMinStockQuery, null);
                    string currentMinStock = result != DBNull.Value ? result.ToString() : string.Empty;

                    // Update the minimum stock value in the database
                    string query = string.Format(update_minimum_stock, minStockValue, productId);
                    await _dbIntegrator.QueryAsync(query, null);
                    MessageBox.Show("Minimum stock value updated successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    // Log the minimum stock update
                    string logEventId = "E001"; // Event ID updates
                    string logQuery = string.Format(insert_log, logEventId, UserSession.UserId, productId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), currentMinStock, minStockValue.ToString(), "min_stock");
                    await _dbIntegrator.QueryAsync(logQuery, null);

                    await LoadDataGridAsync(productId); // Refresh the DataGridView and reselect the row
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Console.WriteLine(ex);
                }
            }
            else
            {
                MessageBox.Show("Please select a product.");
            }
        }


        private async void RefreshTimer_Tick(object sender, EventArgs e)
        {
            await PerformDBSweepAsync();
            await RefreshProductDataGridAsync();
        }

        private async Task RefreshProductDataGridAsync()
        {
            StoreCurrentSearchAndScrollPosition();

            string searchTerm = _currentSearchTerm;
            int? selectedProductId = null;
            int? selectedRowIndex = null;

            if (Product_List.SelectedRows.Count > 0)
            {
                var selectedRow = Product_List.SelectedRows[0];
                if (selectedRow.Cells["id"].Value != DBNull.Value)
                {
                    selectedProductId = Convert.ToInt32(selectedRow.Cells["id"].Value);
                    selectedRowIndex = selectedRow.Index;
                }
            }

            await LoadDataGridAsync(selectedProductId);

            if (!string.IsNullOrEmpty(searchTerm))
            {
                string searchQuery = string.Format(search_function, searchTerm.ToLower());
                DataTable dataTable = await _dbIntegrator.GetDataTableAsync(searchQuery, null);
                Product_List.DataSource = dataTable;
            }

            if (selectedRowIndex.HasValue && selectedRowIndex < Product_List.Rows.Count)
            {
                Product_List.Rows[selectedRowIndex.Value].Selected = true;
                Product_List.FirstDisplayedScrollingRowIndex = selectedRowIndex.Value;
            }

            RestoreSearchAndScrollPosition();
        }


        private async Task LoadDataGridAsync(int? selectedProductId = null)
        {
            StoreCurrentSearchAndScrollPosition();

            // Assuming select_Product doesn't include an ORDER BY already
            string query = @"
        SELECT p.id, p.model_number, p.alias, p.type, p.quantity, p.barcode, p.require_serial_number, p.image_url, s.name AS supplier, p.supplier_link, p.min_stock, p.bin 
        FROM product p 
        LEFT JOIN supplier s ON p.supplier_id = s.id 
        ORDER BY p.id ASC";

            DataTable dataTable = await _dbIntegrator.GetDataTableAsync(query, null);

            Product_List.DataSource = dataTable;

            // Hide the id column
            if (Product_List.Columns.Contains("id"))
            {
                Product_List.Columns["id"].Visible = false;
            }

            // Disable sorting for all columns
            foreach (DataGridViewColumn column in Product_List.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

            if (selectedProductId.HasValue)
            {
                foreach (DataGridViewRow row in Product_List.Rows)
                {
                    if (row.Cells["id"].Value != DBNull.Value && Convert.ToInt32(row.Cells["id"].Value) == selectedProductId.Value)
                    {
                        Product_List.ClearSelection();
                        row.Selected = true;
                        Product_List.FirstDisplayedScrollingRowIndex = row.Index;
                        break;
                    }
                }
            }

            UpdateDecreaseButtonState();

            // Make cells read-only for viewers
            if (UserSession.Role == "Viewer")
            {
                foreach (DataGridViewRow row in Product_List.Rows)
                {
                    foreach (DataGridViewCell cell in row.Cells)
                    {
                        cell.ReadOnly = true;
                    }
                }
            }

            RestoreSearchAndScrollPosition();
        }







        private void UpdateDecreaseButtonState()
        {
            try
            {


                if (Product_List.SelectedRows.Count > 0)
                {
                    int quantity = Convert.ToInt32(Product_List.SelectedRows[0].Cells["quantity"].Value);
                    quantityDown.Enabled = quantity > 0;
                }
            }
            catch (Exception e)
            {

            }
        }

        private void Product_List_DataBindingComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            _blinkingRows.Clear();

            foreach (DataGridViewRow row in Product_List.Rows)
            {
                if (row.Cells["quantity"].Value != DBNull.Value && row.Cells["min_stock"].Value != DBNull.Value)
                {
                    int quantity = Convert.ToInt32(row.Cells["quantity"].Value);
                    int minStock = Convert.ToInt32(row.Cells["min_stock"].Value);

                    if (quantity <= 0)
                    {
                        row.DefaultCellStyle.BackColor = Color.Red;
                        quantityDown.Enabled = false; // Disable the Decrease Quantity button
                    }
                    else if (quantity < minStock)
                    {
                        row.DefaultCellStyle.BackColor = Color.White;
                        _blinkingRows.Add(row);
                    }
                    else
                    {
                        row.DefaultCellStyle.BackColor = Color.White;
                    }
                }
            }
        }

        private void Product_List_SelectionChanged(object sender, EventArgs e)
        {
            UpdateDecreaseButtonState();
        }

        private void BlinkTimer_Tick(object sender, EventArgs e)
        {
            foreach (var row in _blinkingRows)
            {
                if (row.DefaultCellStyle.BackColor == Color.White)
                {
                    row.DefaultCellStyle.BackColor = Color.Yellow;
                }
                else
                {
                    row.DefaultCellStyle.BackColor = Color.White;
                }
            }
        }

        private async Task LoadTypesAsync()
        {
            string query = "SELECT DISTINCT type FROM product";
            DataTable dataTable = await _dbIntegrator.GetDataTableAsync(query, null);
            typeComboBox.Items.Clear();
            foreach (DataRow row in dataTable.Rows)
            {
                typeComboBox.Items.Add(row["type"].ToString());
            }
        }

        private async Task LoadSuppliersAsync()
        {
            string query = select_Supplier;
            DataTable dataTable = await _dbIntegrator.GetDataTableAsync(query, null);
            supplierSelectionBox.Items.Clear();
            foreach (DataRow row in dataTable.Rows)
            {
                supplierSelectionBox.Items.Add(row["name"].ToString());
            }
        }

        private async void searchButton_Click(object sender, EventArgs e)
        {
            StoreCurrentSearchAndScrollPosition();
            string searchTerm = searchTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(searchTerm))
            {
                try
                {
                    DataTable dataTable = await _dbIntegrator.GetDataTableAsync(string.Format(search_function, searchTerm.ToLower()), null);
                    Product_List.DataSource = dataTable;

                    if (dataTable.Rows.Count == 0)
                    {
                        MessageBox.Show("No results found.");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"An error occurred: {ex.Message}");
                    Console.WriteLine(ex);
                }
            }
            else
            {
                await LoadDataGridAsync();
            }
            RestoreSearchAndScrollPosition();
        }


        private async void Product_List_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (e.RowIndex >= 0)
                {
                    int productId = Convert.ToInt32(Product_List.Rows[e.RowIndex].Cells["id"].Value);

                    // Check if the double-clicked cell is the image URL column
                    if (Product_List.Columns[e.ColumnIndex].Name == "image_url")
                    {
                        string imageUrl = Product_List.Rows[e.RowIndex].Cells["image_url"].Value.ToString();
                        if (!string.IsNullOrEmpty(imageUrl))
                        {
                            string imagePath = Path.Combine(ConfigurationManager.AppSettings["ImageServerPath"], imageUrl);
                            if (File.Exists(imagePath))
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                                {
                                    FileName = imagePath,
                                    UseShellExecute = true,
                                    Verb = "open"
                                });
                            }
                            else
                            {
                                MessageBox.Show("Image file not found.");
                            }
                        }
                    }
                    // Check if the double-clicked cell is the supplier link column
                    else if (Product_List.Columns[e.ColumnIndex].Name == "supplier_link")
                    {
                        string supplierLink = Product_List.Rows[e.RowIndex].Cells["supplier_link"].Value.ToString();
                        if (!string.IsNullOrEmpty(supplierLink))
                        {
                            try
                            {
                                // Ensure the link is opened in the default web browser
                                var psi = new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = supplierLink,
                                    UseShellExecute = true
                                };

                                // Check if the link starts with HTTP or HTTPS to ensure it is a URL
                                if (supplierLink.StartsWith("http://") || supplierLink.StartsWith("https://") || supplierLink.StartsWith("www."))
                                {
                                    System.Diagnostics.Process.Start(psi);
                                }
                                else
                                {
                                    MessageBox.Show("Invalid URL. Please ensure the supplier link starts with http://, https://, or www.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"An error occurred: {ex.Message}");
                                Console.WriteLine(ex);
                            }
                        }
                    }
                    else
                    {
                        bool requireSerialNumber = Convert.ToBoolean(Product_List.Rows[e.RowIndex].Cells["require_serial_number"].Value);

                        if (!requireSerialNumber)
                        {
                            return;
                        }

                        string query = string.Format(select_All_From_History, productId);

                        try
                        {
                            DataTable dataTable = await _dbIntegrator.GetDataTableAsync(query, null);
                            Inventory_Manager.History historyForm = new Inventory_Manager.History(productId, dataTable);
                            historyForm.Show();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"An error occurred: {ex.Message}");
                            Console.WriteLine(ex);
                        }
                    }
                }
            }
            catch { }
        }

        private async void increaseQuantityButton_Click(object sender, EventArgs e)
        {
            await UpdateProductQuantityAsync(true);
        }

        private async void decreaseQuantityButton_Click(object sender, EventArgs e)
        {
            if (Product_List.SelectedRows.Count > 0)
            {
                int productId = Convert.ToInt32(Product_List.SelectedRows[0].Cells["id"].Value);
                string requireSerialNumberQuery = "SELECT require_serial_number FROM product WHERE id = " + productId;
                object result = await _dbIntegrator.SelectAsync(requireSerialNumberQuery, null);
                bool requireSerialNumber = Convert.ToBoolean(result);

                if (requireSerialNumber)
                {
                    MessageBox.Show("Quantity cannot be decreased for products that track serial numbers.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                else
                {
                    await UpdateProductQuantityAsync(false);
                }
            }
            else
            {
                MessageBox.Show("Please select a product.");
            }
        }


        private async Task UpdateProductQuantityAsync(bool isIncrease)
        {
            if (Product_List.SelectedRows.Count > 0)
            {
                int productId = Convert.ToInt32(Product_List.SelectedRows[0].Cells["id"].Value);
                if (!int.TryParse(QuantityChangeAmtBox.Text.Trim(), out int quantityChange) || quantityChange <= 0)
                {
                    MessageBox.Show("Please enter a valid quantity.");
                    return;
                }

                if (!isIncrease)
                {
                    quantityChange = -quantityChange;
                }

                string requireSerialNumberQuery = "SELECT require_serial_number FROM product WHERE id = " + productId;
                object result = await _dbIntegrator.SelectAsync(requireSerialNumberQuery, null);
                bool requireSerialNumber = Convert.ToBoolean(result);

                if (quantityChange > 0 && requireSerialNumber)
                {
                    List<string> serialNumbers = await PromptForSerialNumbers(quantityChange);
                    if (serialNumbers.Count != quantityChange)
                    {
                        return;
                    }
                    foreach (string serialNumber in serialNumbers)
                    {
                        string insertHistory = string.Format(insert_data_into_history_if_not_exists, productId, 1, serialNumber, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "");
                        await _dbIntegrator.QueryAsync(insertHistory, null);
                    }
                }

                string oldQuantityQuery = $"SELECT quantity FROM product WHERE id = {productId}";
                object oldQuantityResult = await _dbIntegrator.SelectAsync(oldQuantityQuery, null);
                int oldQuantity = Convert.ToInt32(oldQuantityResult);

                string query = string.Format(updateQuantity, quantityChange, productId);
                await _dbIntegrator.QueryAsync(query, null);

                // Determine event type based on increase or decrease
                string eventId = isIncrease ? "E002" : "E004";

                // Log the quantity change event
                string newQuantityQuery = $"SELECT quantity FROM product WHERE id = {productId}";
                object newQuantityResult = await _dbIntegrator.SelectAsync(newQuantityQuery, null);
                int newQuantity = Convert.ToInt32(newQuantityResult);

                string logQuantityChangeEvent = string.Format(insert_log, eventId, UserSession.UserId, productId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), oldQuantity.ToString(), newQuantity.ToString(), "quantity");
                await _dbIntegrator.QueryAsync(logQuantityChangeEvent, null);

                await LoadDataGridAsync(productId); // Refresh the DataGridView and reselect the row
            }
            else
            {
                MessageBox.Show("Please select a product.");
            }
        }



        private async void addLocationButton_Click(object sender, EventArgs e)
        {
            string locationName = locationNameTextBox.Text.Trim();

            if (string.IsNullOrEmpty(locationName))
            {
                MessageBox.Show("Please enter a location name.");
                return;
            }

            string query = string.Format(insert_location, locationName);

            try
            {
                await _dbIntegrator.QueryAsync(query, null);
                MessageBox.Show("Location added successfully.");
                locationNameTextBox.Clear();
                await LoadLocationsIntoComboBoxAsync(); // Refresh the ComboBox
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}");
                Console.WriteLine(ex);
            }
        }

        private async void addButton_Click(object sender, EventArgs e)
        {
            string modelNumber = modelNumberTextBox.Text.Trim();
            string alias = productAlias.Text.Trim();
            string type = typeComboBox.Text.Trim();
            int quantity;
            string barcode = barcodeTextBox.Text.Trim();
            bool requireSerialNumber = requireSerialNumberCheckBox.Checked;
            string supplier = supplierSelectionBox.Text.Trim();
            string supplierLink = supplierLinkBox.Text.Trim();
            int? minStock = null;
            string bin = BinTextBox.Text.Trim();

            if (string.IsNullOrEmpty(modelNumber) || string.IsNullOrEmpty(type) || !int.TryParse(quantityTextBox.Text.Trim(), out quantity) || string.IsNullOrEmpty(barcode))
            {
                MessageBox.Show("Please fill in all product details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (int.TryParse(MinimumStockTextBox.Text.Trim(), out int minStockValue))
            {
                minStock = minStockValue;
            }

            try
            {
                int? supplierId = null;
                if (!string.IsNullOrEmpty(supplier))
                {
                    // Check if the supplier exists
                    string checkSupplierQuery = $"SELECT id FROM supplier WHERE name = '{supplier}'";
                    object result = await _dbIntegrator.SelectAsync(checkSupplierQuery, null);

                    if (result != null)
                    {
                        supplierId = Convert.ToInt32(result);
                    }
                    else
                    {
                        // Insert new supplier if it doesn't exist
                        string insertSupplierQuery = string.Format(insert_supplier, supplier);
                        result = await _dbIntegrator.SelectAsync(insertSupplierQuery, null);
                        supplierId = Convert.ToInt32(result);
                    }
                }

                string supplierIdValueString = supplierId.HasValue ? supplierId.Value.ToString() : "NULL";
                string minStockValueString = minStock.HasValue ? minStock.Value.ToString() : "NULL";

                string productAdd = string.Format(product_add, modelNumber, alias, type, quantity, barcode, requireSerialNumber, supplierIdValueString, supplierLink, minStockValueString, bin);
                object newProductIdObj = await _dbIntegrator.SelectAsync(productAdd, null);
                int newProductId = Convert.ToInt32(newProductIdObj);

                if (requireSerialNumber)
                {
                    List<string> serialNumbers = await PromptForSerialNumbers(quantity);
                    if (serialNumbers.Count != quantity)
                    {
                        string deleteProductQuery = $"DELETE FROM product WHERE id = {newProductId}";
                        await _dbIntegrator.QueryAsync(deleteProductQuery, null);
                        return;
                    }
                    foreach (string serialNumber in serialNumbers)
                    {
                        string insertHistory = string.Format(insert_data_into_history_if_not_exists, newProductId, 1, serialNumber, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "");
                        await _dbIntegrator.QueryAsync(insertHistory, null);
                    }
                }

                // Log the create event
                string logCreateEvent = string.Format(insert_log, "E003", UserSession.UserId, newProductId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "NULL", modelNumber, "model_number");
                await _dbIntegrator.QueryAsync(logCreateEvent, null);

                await LoadDataGridAsync(); // Refresh the DataGridView
                await LoadTypesAsync(); // Reload the types in ComboBox
                MessageBox.Show("Product added successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ClearProductFormFields();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.WriteLine(ex);
            }
        }


        private async void deleteButton_Click(object sender, EventArgs e)
        {
            if (Product_List.SelectedRows.Count > 0)
            {
                int productId = Convert.ToInt32(Product_List.SelectedRows[0].Cells["id"].Value);
                string modelNumber = Product_List.SelectedRows[0].Cells["model_number"].Value.ToString();
                DialogResult result = MessageBox.Show("Are you sure you want to delete this product?", "Confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result == DialogResult.Yes)
                {
                    try
                    {
                        string deleteQuery = $"DELETE FROM product WHERE id = {productId}";
                        await _dbIntegrator.QueryAsync(deleteQuery, null);

                        // Log the delete event
                        string logDeleteEvent = string.Format(insert_log, "E004", UserSession.UserId, productId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), modelNumber, "NULL", "model_number");
                        await _dbIntegrator.QueryAsync(logDeleteEvent, null);

                        await LoadDataGridAsync(); // Refresh the DataGridView
                        MessageBox.Show("Product deleted successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Console.WriteLine(ex);
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select a product to delete.");
            }
        }





        private async void SupplierAddButton_Click(object sender, EventArgs e)
        {
            string supplierName = supplierSelectionBox.Text.Trim();

            if (string.IsNullOrEmpty(supplierName))
            {
                MessageBox.Show("Please enter a supplier name.");
                return;
            }

            try
            {
                // Check if the supplier already exists
                string checkSupplierQuery = $"SELECT id FROM supplier WHERE name = '{supplierName}'";
                object result = await _dbIntegrator.SelectAsync(checkSupplierQuery, null);

                int supplierId;
                if (result != null)
                {
                    supplierId = Convert.ToInt32(result);
                }
                else
                {
                    // Insert new supplier if it doesn't exist
                    string insertSupplierQuery = string.Format(insert_supplier, supplierName);
                    result = await _dbIntegrator.SelectAsync(insertSupplierQuery, null);
                    supplierId = Convert.ToInt32(result);
                }

                // Associate supplier with selected product
                if (Product_List.SelectedRows.Count > 0)
                {
                    int productId = Convert.ToInt32(Product_List.SelectedRows[0].Cells["id"].Value);
                    string oldSupplierQuery = $"SELECT supplier_id FROM product WHERE id = {productId}";
                    object oldSupplierResult = await _dbIntegrator.SelectAsync(oldSupplierQuery, null);
                    int? oldSupplierId = oldSupplierResult != DBNull.Value ? Convert.ToInt32(oldSupplierResult) : (int?)null;

                    string associateQuery = string.Format(associate_product_supplier, supplierId, productId);
                    await _dbIntegrator.QueryAsync(associateQuery, null);

                    // Log the supplier association event
                    string oldSupplierName = oldSupplierId.HasValue ? (await _dbIntegrator.SelectAsync($"SELECT name FROM supplier WHERE id = {oldSupplierId}", null)).ToString() : "NULL";
                    string logSupplierAssociationEvent = string.Format(insert_log, "E001", UserSession.UserId, productId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), oldSupplierName, supplierName, "supplier");
                    await _dbIntegrator.QueryAsync(logSupplierAssociationEvent, null);

                    MessageBox.Show("Supplier associated with the selected product successfully.");
                    await LoadDataGridAsync(productId); // Refresh the DataGridView and reselect the row
                }
                else
                {
                    MessageBox.Show("Supplier added successfully.");
                }

                // Refresh the supplier selection box
                await LoadSuppliersAsync();
                supplierSelectionBox.Text = supplierName;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}");
                Console.WriteLine(ex);
            }
        }


        private async void SupplierLinkButton_Click(object sender, EventArgs e)
        {
            if (Product_List.SelectedRows.Count > 0)
            {
                int productId = Convert.ToInt32(Product_List.SelectedRows[0].Cells["id"].Value);
                string supplierLink = supplierLinkBox.Text.Trim();

                if (string.IsNullOrEmpty(supplierLink) ||
                    !(supplierLink.StartsWith("http://") || supplierLink.StartsWith("https://") || supplierLink.StartsWith("www.")))
                {
                    MessageBox.Show("Please enter a valid supplier link that starts with http://, https://, or www.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string query = string.Format(update_supplier_link, supplierLink, productId);
                await _dbIntegrator.QueryAsync(query, null);
                MessageBox.Show("Supplier link updated successfully.");
                await LoadDataGridAsync(productId); // Refresh the DataGridView and reselect the row
            }
            else
            {
                MessageBox.Show("Please select a product.");
            }
        }

        private async void BinSet_Click(object sender, EventArgs e)
        {
            if (Product_List.SelectedRows.Count > 0)
            {
                int productId = Convert.ToInt32(Product_List.SelectedRows[0].Cells["id"].Value);
                string newBin = BinTextBox.Text.Trim();

                if (string.IsNullOrEmpty(newBin))
                {
                    MessageBox.Show("Please enter a valid bin value.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Fetch the current bin value from the database
                string currentBinQuery = $"SELECT bin FROM product WHERE id = {productId}";
                object result = await _dbIntegrator.SelectAsync(currentBinQuery, null);
                string currentBin = result != DBNull.Value ? result.ToString() : string.Empty;

                // Update the bin value in the database
                string updateQuery = string.Format(update_bin, newBin, productId);
                await _dbIntegrator.QueryAsync(updateQuery, null);
                MessageBox.Show("Bin value updated successfully.");

                // Log the bin update
                string logEventId = "E001"; // Event ID for bin updates
                string logQuery = string.Format(insert_log, logEventId, UserSession.UserId, productId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), currentBin, newBin, "bin");
                await _dbIntegrator.QueryAsync(logQuery, null);

                // Refresh the DataGridView and reselect the row
                await LoadDataGridAsync(productId);
            }
            else
            {
                MessageBox.Show("Please select a product.");
            }
        }


        private async Task<List<Tuple<int, string>>> GetLocationsAsync()
        {
            string query = "SELECT id, name FROM location";
            DataTable dataTable = await _dbIntegrator.GetDataTableAsync(query, null);
            return dataTable.AsEnumerable().Select(row => new Tuple<int, string>(row.Field<int>("id"), row.Field<string>("name"))).ToList();
        }



        private async Task<List<string>> PromptForSerialNumbers(int count)
        {
            List<string> serialNumbers = new List<string>();
            for (int i = 0; i < count; i++)
            {
                using (var inputForm = new SerialNumberInputForm(i + 1, count))
                {
                    if (inputForm.ShowDialog() == DialogResult.OK)
                    {
                        if (!string.IsNullOrEmpty(inputForm.EnteredSerialNumber))
                        {
                            // Check against the list of already entered serial numbers
                            if (serialNumbers.Contains(inputForm.EnteredSerialNumber))
                            {
                                MessageBox.Show("Serial number already entered in this session. Please enter a different serial number.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                i--;
                                continue;
                            }

                            // Check against the database
                            string query = $"SELECT COUNT(*) FROM history WHERE serial_number = '{inputForm.EnteredSerialNumber}'";
                            object result = await _dbIntegrator.SelectAsync(query, null);
                            int countExisting = Convert.ToInt32(result);

                            if (countExisting > 0)
                            {
                                MessageBox.Show("Serial number already exists in the database. Please enter a different serial number.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                i--;
                            }
                            else
                            {
                                serialNumbers.Add(inputForm.EnteredSerialNumber);
                            }
                        }
                        else
                        {
                            MessageBox.Show("Serial number cannot be empty. Please enter a valid serial number.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            i--;
                        }
                    }
                    else
                    {
                        return new List<string>();
                    }
                }
            }
            return serialNumbers;
        }

        private void ClearProductFormFields()
        {
            modelNumberTextBox.Clear();
            productAlias.Clear();
            typeComboBox.SelectedIndex = -1;
            quantityTextBox.Clear();
            barcodeTextBox.Clear();
            requireSerialNumberCheckBox.Checked = false;
            supplierSelectionBox.SelectedIndex = -1;
            supplierLinkBox.Clear();
            MinimumStockTextBox.Clear();
            BinTextBox.Clear();
        }

        private async void DBSweepButton_Click(object sender, EventArgs e)
        {
            await PerformDBSweepAsync();
        }

        private async Task PerformDBSweepAsync()
        {
            try
            {
                // Query to get all products that require serial numbers
                string query = "SELECT id FROM product WHERE require_serial_number = true";
                DataTable dataTable = await _dbIntegrator.GetDataTableAsync(query, null);

                foreach (DataRow row in dataTable.Rows)
                {
                    int productId = Convert.ToInt32(row["id"]);

                    // Query to count the number of serial numbers for this product in the stock location (id_location = 1)
                    string countQuery = $"SELECT COUNT(*) FROM history WHERE id_product = {productId} AND id_location = 1";
                    object result = await _dbIntegrator.SelectAsync(countQuery, null);

                    // Check if the result is valid
                    if (result != null && int.TryParse(result.ToString(), out int serialNumberCount))
                    {
                        // Update the product quantity to match the count of serial numbers in stock
                        string updateQuery = $"UPDATE product SET quantity = {serialNumberCount} WHERE id = {productId}";
                        await _dbIntegrator.QueryAsync(updateQuery, null);
                    }
                    else
                    {
                        MessageBox.Show($"Count query result for product ID {productId} is invalid or null.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }

                //MessageBox.Show("DB Sweep completed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during DB Sweep: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.WriteLine($"An error occurred during DB Sweep: {ex}");
            }
        }

        private async void ImageUploadButton_Click(object sender, EventArgs e)
        {
            if (Product_List.SelectedRows.Count > 0)
            {
                int productId = Convert.ToInt32(Product_List.SelectedRows[0].Cells["id"].Value);

                using (OpenFileDialog openFileDialog = new OpenFileDialog())
                {
                    openFileDialog.Filter = "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp";
                    DialogResult result = openFileDialog.ShowDialog();

                    if (result == DialogResult.OK)
                    {
                        try
                        {
                            string imageServerPath = ConfigurationManager.AppSettings["ImageServerPath"];
                            string imageName = $"{productId}{Path.GetExtension(openFileDialog.FileName)}";
                            string destPath = Path.Combine(imageServerPath, imageName);

                            File.Copy(openFileDialog.FileName, destPath, true);

                            string updateImageUrlQuery = $"UPDATE product SET image_url = '{imageName}' WHERE id = {productId}";
                            await _dbIntegrator.QueryAsync(updateImageUrlQuery, null);

                            await LoadDataGridAsync(productId);
                            MessageBox.Show("Image uploaded successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            Console.WriteLine(ex);
                        }
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select a product to upload an image.");
            }
        }
        private async void QTAuth_Click(object sender, EventArgs e)
        {
            Button button = (Button)sender;
            button.Enabled = false;

            string username = QTUsername.Text;
            string password = QTPassword.Text;
            string subdomain = "AIS";

            //md5 the password
            StringBuilder sb = new StringBuilder();
            using (MD5 md5 = MD5.Create())
            {
                byte[] hashValue = md5.ComputeHash(Encoding.UTF8.GetBytes(password));
                foreach (byte b in hashValue)
                {
                    sb.Append($"{b:X2}");
                }
                password = sb.ToString();
                password = password.ToLower();
            }

            //returns true if valid log in and false for bad
            bool loggedIn = await ValidateLogin(username, password, subdomain);
        }

        public async Task<bool> ValidateLogin(string username, string password, string subdomain)
        {
            string api = "app.quicktech.com";
            string apiUrl = "https://" + api + "/auth/mobile";
            using var httpClient = new HttpClient();
            try
            {
                var payload = new
                {
                    username,
                    password,
                    companyDomain = subdomain
                };
                string json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(apiUrl, content);
                if (response.IsSuccessStatusCode)
                {
                    string responseJson = await response.Content.ReadAsStringAsync();
                    var responseObject = JsonConvert.DeserializeObject<LoginResponse>(responseJson);

                    if (responseObject != null)
                    {
                        string authValue = responseObject.auth;
                        if (authValue == null)
                        {
                            //no 2fa
                            //keep this token
                            _JSESSIONID = responseObject.JSESSIONID;
                        }
                        else
                        {
                            //with 2fa
                            //user it for 2fa api
                            QT2FA.Visible = true;
                            QT2FAAUTH.Visible = true;
                            _JSESSIONID = responseObject.JSESSIONID;
                            _AUTH = responseObject.auth;
                        }
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private class LoginResponse
        {
            public string JSESSIONID { get; set; }
            public string auth { get; set; }
        }

        private async void QT2FAAUTH_Click(object sender, EventArgs e)
        {
            string verifCode = QT2FA.Text;
            if (verifCode == null || verifCode == "" || verifCode.Length != 6)
            {
                //entry is empty
                return;
            }
            string apiUrl = "https://" + "app.quicktech.com" + "/code/mobile";
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Cookie", "JSESSIONID=" + _JSESSIONID);
            int verifCodeInt = Convert.ToInt32(verifCode);
            bool isGoogle = false;
            if (_AUTH == "google")
            {
                isGoogle = true;
            }
            else { }
            var payload2fa = new
            {
                code = verifCodeInt,
                google = isGoogle
            };
            string json = JsonConvert.SerializeObject(payload2fa);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(apiUrl, content);
            try
            {
                if (response.IsSuccessStatusCode)
                {
                    //success
                    QTPULLLOC.Visible = true;
                }
                else
                {
                    //no worky
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private async void QTPULLLOC_Click(object sender, EventArgs e)
        {
            pullLocLoadingBar.Visible = true; // Show the progress bar
            pullLocLoadingBar.Style = ProgressBarStyle.Marquee; // Set style to marquee initially

            //pull location
            List<CompanyInformation> clients = await GetClients();
            clients = clients.OrderBy(x => x.name).ToList();

            pullLocLoadingBar.Style = ProgressBarStyle.Continuous; // Change to continuous style
            pullLocLoadingBar.Maximum = clients.Count; // Set maximum value
            pullLocLoadingBar.Value = 0; // Reset progress bar

            foreach (var client in clients)
            {
                string query = string.Format(insert_location, client.name);

                try
                {
                    await _dbIntegrator.QueryAsync(query, null);
                    pullLocLoadingBar.Value += 1; // Increment the progress bar
                    //MessageBox.Show("Location added successfully.");
                    locationNameTextBox.Clear();
                }
                catch (Exception ex)
                {
                    //MessageBox.Show($"An error occurred: {ex.Message}");
                    Console.WriteLine(ex);
                }
            }

            pullLocLoadingBar.Visible = false; // Hide the progress bar
        }

        public async Task<List<CompanyInformation>> GetClients()
        {
            HttpClient _httpClient = new();
            string endpoint = "https://app.quicktech.com/company/clients";
            try
            {
                _httpClient.DefaultRequestHeaders.Add("Cookie", "JSESSIONID=" + _JSESSIONID);
                var payloadData = new { needCompanyInfo = true, isArchived = false };
                string payloadJson = JsonConvert.SerializeObject(payloadData);
                HttpContent content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync(endpoint, content);
                Console.WriteLine(response);
                if (response.StatusCode == HttpStatusCode.Locked)
                {
                    return await GetClients();
                }
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync();
                List<CompanyInformation> clients = JsonConvert.DeserializeObject<List<CompanyInformation>>(json);
                return clients;
            }
            catch (JsonSerializationException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching ticket: {ex.Message}");
                return null;
            }
            finally
            {
                _httpClient.Dispose();
            }
        }

        private void OnProcessExit(object sender, EventArgs e)
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        private void BarcodeGen_Click(object sender, EventArgs e)
        {
            if (Product_List.SelectedRows.Count > 0)
            {
                string barcode = Product_List.SelectedRows[0].Cells["barcode"].Value?.ToString();
                string alias = Product_List.SelectedRows[0].Cells["alias"].Value?.ToString();

                if (!string.IsNullOrEmpty(barcode))
                {
                    GenerateBarcode(barcode, alias);
                }
                else
                {
                    MessageBox.Show("The selected product does not have a barcode.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                MessageBox.Show("Please select a product.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void GenerateBarcode(string barcodeText, string alias)
        {
            var barcodeWriter = new BarcodeWriter<Bitmap>
            {
                Format = BarcodeFormat.CODE_128,
                Options = new EncodingOptions
                {
                    Width = _labelConfig.Width,
                    Height = _labelConfig.Length,
                    Margin = 10
                },
                Renderer = new CustomBitmapRenderer() // Use the custom renderer
            };

            _barcodeBitmap = barcodeWriter.Write($"{alias}|{barcodeText}");

            string filePath = Path.Combine(_tempDirectory, $"{barcodeText}.png");
            _barcodeBitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);

            // Show print dialog
            PrintDocument printDoc = new PrintDocument();
            printDoc.PrintPage += new PrintPageEventHandler(PrintDoc_PrintPage);
            PrintDialog printDialog = new PrintDialog();
            printDialog.Document = printDoc;
            if (printDialog.ShowDialog() == DialogResult.OK)
            {
                printDoc.Print();
            }
        }

        private void PrintLocationName(PrintPageEventArgs e, string locationName)
        {
            // Load calibrated label dimensions
            int labelWidth = _labelConfig.Width;
            int labelHeight = _labelConfig.Length;

            // Initialize the font with a starting size
            float fontSize = 10; // Start with a reasonable font size
            Font font = new Font("Arial", fontSize);

            // Measure the size of the text
            SizeF textSize = e.Graphics.MeasureString(locationName, font);

            // Increase the font size until the text no longer fits within the label dimensions
            while (textSize.Width < labelWidth && textSize.Height < labelHeight)
            {
                fontSize += 1;
                font = new Font("Arial", fontSize);
                textSize = e.Graphics.MeasureString(locationName, font);
            }

            // Reduce the font size by one to ensure it fits
            fontSize -= 1;
            font = new Font("Arial", fontSize);
            textSize = e.Graphics.MeasureString(locationName, font);

            // If the text still doesn't fit, reduce the font size until it fits
            while (textSize.Width > labelWidth || textSize.Height > labelHeight)
            {
                fontSize -= 1;
                font = new Font("Arial", fontSize);
                textSize = e.Graphics.MeasureString(locationName, font);
            }

            // Define the position for the text
            float x = (labelWidth - textSize.Width) / 2; // Center the text horizontally
            float y = (labelHeight - textSize.Height) / 2; // Center the text vertically

            // Draw the location name
            e.Graphics.DrawString(locationName, font, Brushes.Black, new PointF(x, y));
        }





        private void PrintDoc_PrintPage(object sender, PrintPageEventArgs e)
        {
            if (_barcodeBitmap != null)
            {
                e.Graphics.DrawImage(_barcodeBitmap, new Point(0, 0));
            }
        }

        private async void SerialNumberReqRemoval_Click(object sender, EventArgs e)
        {
            if (Product_List.SelectedRows.Count > 0)
            {
                DialogResult result = MessageBox.Show(
                    "Are you sure you want to remove the serial number requirement?",
                    "Confirmation",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (result == DialogResult.Yes)
                {
                    // Disable the button to prevent multiple clicks
                    SerialNumberReqRemoval.Enabled = false;

                    // Get the selected product ID
                    int productId = Convert.ToInt32(Product_List.SelectedRows[0].Cells["id"].Value);

                    try
                    {
                        // Update the product to remove the serial number requirement
                        string query = string.Format(update_require_serial_number, false, productId);
                        await _dbIntegrator.QueryAsync(query, null);

                        MessageBox.Show("Serial number requirement removed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        await LoadDataGridAsync(productId); // Refresh the DataGridView and reselect the row

                        // Log the update event
                        string logUpdateEvent = string.Format(insert_log, "E001", UserSession.UserId, productId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "Enabled", "Disabled", "Serial Number Requirement");
                        await _dbIntegrator.QueryAsync(logUpdateEvent, null);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Console.WriteLine(ex);
                    }
                    finally
                    {
                        // Re-enable the button
                        SerialNumberReqRemoval.Enabled = true;
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select a product.");
            }
        }

        private async void EnableSerialNumberReq_Click(object sender, EventArgs e)
        {
            if (Product_List.SelectedRows.Count > 0)
            {
                DialogResult result = MessageBox.Show(
                    "Are you sure you want to enable the serial number requirement?",
                    "Confirmation",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (result == DialogResult.Yes)
                {
                    // Disable the button to prevent multiple clicks
                    EnableSerialNumberReq.Enabled = false;

                    // Get the selected product ID
                    int productId = Convert.ToInt32(Product_List.SelectedRows[0].Cells["id"].Value);

                    try
                    {
                        // Update the product to enable the serial number requirement
                        string query = string.Format(update_require_serial_number, true, productId);
                        await _dbIntegrator.QueryAsync(query, null);

                        MessageBox.Show("Serial number requirement enabled successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        await LoadDataGridAsync(productId); // Refresh the DataGridView and reselect the row

                        // Log the update event
                        string logUpdateEvent = string.Format(insert_log, "E001", UserSession.UserId, productId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "Disabled", "Enabled", "Serial Number Requirement");
                        await _dbIntegrator.QueryAsync(logUpdateEvent, null);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Console.WriteLine(ex);
                    }
                    finally
                    {
                        // Re-enable the button
                        EnableSerialNumberReq.Enabled = true;
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select a product.");
            }
        }

        private async void SelectViaScan_Click(object sender, EventArgs e)
        {
            using (var barcodeInputForm = new BarcodeInputForm())
            {
                while (true)
                {
                    if (barcodeInputForm.ShowDialog() == DialogResult.OK)
                    {
                        string scannedBarcode = barcodeInputForm.EnteredBarcode;
                        if (string.IsNullOrEmpty(scannedBarcode))
                        {
                            MessageBox.Show("Please enter a valid barcode.");
                            continue;
                        }

                        // Check if the barcode exists in the system
                        string query = $"SELECT * FROM product WHERE barcode = '{scannedBarcode}'";
                        DataTable dataTable = await _dbIntegrator.GetDataTableAsync(query, null);

                        if (dataTable.Rows.Count > 0)
                        {
                            var productRow = dataTable.Rows[0];
                            string alias = productRow["alias"].ToString(); // Get the alias instead of the barcode

                            // Show the confirmation dialog with the alias
                            var confirmResult = MessageBox.Show(
                                $"Is this the correct product: {alias}?",
                                "Confirm Product",
                                MessageBoxButtons.YesNo);

                            if (confirmResult == DialogResult.Yes)
                            {
                                // Handle adding or removing stock
                                bool requireSerialNumber = Convert.ToBoolean(productRow["require_serial_number"]);

                                if (requireSerialNumber)
                                {
                                    // Handle serial number tracked product
                                    var quantityForm = new QuantityForm();
                                    if (quantityForm.ShowDialog() == DialogResult.OK)
                                    {
                                        int quantity = quantityForm.Quantity;
                                        await UpdateProductQuantityAsync(true, quantity, scannedBarcode, requireSerialNumber);
                                    }
                                    else
                                    {
                                        continue;
                                    }
                                }
                                else
                                {
                                    // Handle non-serial number tracked product
                                    var quantityForm = new QuantityForm();
                                    if (quantityForm.ShowDialog() == DialogResult.OK)
                                    {
                                        int quantity = quantityForm.Quantity;

                                        var addRemoveResult = MessageBox.Show(
                                            "Are you adding to stock?",
                                            "Add or Remove",
                                            MessageBoxButtons.YesNoCancel);

                                        if (addRemoveResult == DialogResult.Yes)
                                        {
                                            await UpdateProductQuantityAsync(true, quantity, scannedBarcode, requireSerialNumber);
                                        }
                                        else if (addRemoveResult == DialogResult.No)
                                        {
                                            await UpdateProductQuantityAsync(false, quantity, scannedBarcode, requireSerialNumber);
                                        }
                                        else
                                        {
                                            continue;
                                        }
                                    }
                                    else
                                    {
                                        continue;
                                    }
                                }
                                break; // Exit the loop if everything is processed correctly
                            }
                        }
                        else
                        {
                            MessageBox.Show("Barcode not found.");
                        }
                    }
                    else
                    {
                        break; // Exit the loop if the user cancels
                    }
                }
            }
        }


        private async Task UpdateProductQuantityAsync(bool isIncrease, int quantity, string barcode, bool requireSerialNumber)
        {
            string query = $"SELECT id, quantity FROM product WHERE barcode = '{barcode}'";
            DataTable dataTable = await _dbIntegrator.GetDataTableAsync(query, null);

            if (dataTable.Rows.Count > 0)
            {
                int productId = Convert.ToInt32(dataTable.Rows[0]["id"]);
                int oldQuantity = Convert.ToInt32(dataTable.Rows[0]["quantity"]);

                if (requireSerialNumber)
                {
                    List<string> serialNumbers = await PromptForSerialNumbers(quantity);
                    if (serialNumbers.Count != quantity)
                    {
                        return;
                    }
                    foreach (string serialNumber in serialNumbers)
                    {
                        string insertHistory = string.Format(insert_data_into_history_if_not_exists, productId, 1, serialNumber, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "");
                        await _dbIntegrator.QueryAsync(insertHistory, null);
                    }
                }

                if (!isIncrease)
                {
                    quantity = -quantity;
                }

                string updateQuery = string.Format(updateQuantity, quantity, productId);
                await _dbIntegrator.QueryAsync(updateQuery, null);

                // Log the quantity change
                int newQuantity = oldQuantity + quantity;
                string logEventId = isIncrease ? "E002" : "E004"; // Event ID for increase or decrease
                string logQuantityChangeEvent = string.Format(insert_log, logEventId, UserSession.UserId, productId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), oldQuantity.ToString(), newQuantity.ToString(), "quantity");
                await _dbIntegrator.QueryAsync(logQuantityChangeEvent, null);

                await LoadDataGridAsync(productId); // Refresh the DataGridView and reselect the row
            }
        }


        private async Task LoadTypesIntoUpdateTypeComboBoxAsync()
        {
            string query = "SELECT DISTINCT type FROM product";
            DataTable dataTable = await _dbIntegrator.GetDataTableAsync(query, null);
            UpdateType.Items.Clear();
            foreach (DataRow row in dataTable.Rows)
            {
                UpdateType.Items.Add(row["type"].ToString());
            }

            UpdateType.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            UpdateType.AutoCompleteSource = AutoCompleteSource.ListItems;
        }


        private async void DB_SWEEP_Click(object sender, EventArgs e)
        {
            await PerformDBSweepAsync();
        }

        private async void setAlias_Click(object sender, EventArgs e)
        {
            if (Product_List.SelectedRows.Count > 0)
            {
                int productId = Convert.ToInt32(Product_List.SelectedRows[0].Cells["id"].Value);
                string newAlias = AliasText.Text.Trim();
                string oldAlias = Product_List.SelectedRows[0].Cells["alias"].Value.ToString();

                if (string.IsNullOrEmpty(newAlias))
                {
                    MessageBox.Show("Please enter a valid alias.");
                    return;
                }

                string query = $"UPDATE product SET alias = '{newAlias}' WHERE id = {productId}";
                await _dbIntegrator.QueryAsync(query, null);

                // Log the update event
                string logUpdateEvent = string.Format(insert_log, "E001", UserSession.UserId, productId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), oldAlias, newAlias, "alias");
                await _dbIntegrator.QueryAsync(logUpdateEvent, null);

                MessageBox.Show("Alias updated successfully.");
                await LoadDataGridAsync(productId); // Refresh the DataGridView and reselect the row
            }
            else
            {
                MessageBox.Show("Please select a product.");
            }
        }


        private async void setModelNum_Click(object sender, EventArgs e)
        {
            if (Product_List.SelectedRows.Count > 0)
            {
                int productId = Convert.ToInt32(Product_List.SelectedRows[0].Cells["id"].Value);
                string newModelNum = ModelNumText.Text.Trim();
                string oldModelNum = Product_List.SelectedRows[0].Cells["model_number"].Value.ToString();

                if (string.IsNullOrEmpty(newModelNum))
                {
                    MessageBox.Show("Please enter a valid model number.");
                    return;
                }

                string query = $"UPDATE product SET model_number = '{newModelNum}' WHERE id = {productId}";
                await _dbIntegrator.QueryAsync(query, null);

                // Log the update event
                string logUpdateEvent = string.Format(insert_log, "E001", UserSession.UserId, productId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), oldModelNum, newModelNum, "model_number");
                await _dbIntegrator.QueryAsync(logUpdateEvent, null);

                MessageBox.Show("Model number updated successfully.");
                await LoadDataGridAsync(productId); // Refresh the DataGridView and reselect the row
            }
            else
            {
                MessageBox.Show("Please select a product.");
            }
        }

        private async void setBarcode_Click(object sender, EventArgs e)
        {
            if (Product_List.SelectedRows.Count > 0)
            {
                int productId = Convert.ToInt32(Product_List.SelectedRows[0].Cells["id"].Value);
                string newBarcode = BarcodeText.Text.Trim();
                string oldBarcode = Product_List.SelectedRows[0].Cells["barcode"].Value.ToString();

                if (string.IsNullOrEmpty(newBarcode))
                {
                    MessageBox.Show("Please enter a valid barcode.");
                    return;
                }

                string query = $"UPDATE product SET barcode = '{newBarcode}' WHERE id = {productId}";
                await _dbIntegrator.QueryAsync(query, null);

                // Log the update event
                string logUpdateEvent = string.Format(insert_log, "E001", UserSession.UserId, productId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), oldBarcode, newBarcode, "barcode");
                await _dbIntegrator.QueryAsync(logUpdateEvent, null);

                MessageBox.Show("Barcode updated successfully.");
                await LoadDataGridAsync(productId); // Refresh the DataGridView and reselect the row
            }
            else
            {
                MessageBox.Show("Please select a product.");
            }
        }

        private async void setType_Click(object sender, EventArgs e)
        {
            if (Product_List.SelectedRows.Count > 0)
            {
                int productId = Convert.ToInt32(Product_List.SelectedRows[0].Cells["id"].Value);
                string newType = UpdateType.Text.Trim();
                string oldType = Product_List.SelectedRows[0].Cells["type"].Value.ToString();

                if (string.IsNullOrEmpty(newType))
                {
                    MessageBox.Show("Please select a valid type.");
                    return;
                }

                string query = $"UPDATE product SET type = '{newType}' WHERE id = {productId}";
                await _dbIntegrator.QueryAsync(query, null);

                // Log the update event
                string logUpdateEvent = string.Format(insert_log, "E001", UserSession.UserId, productId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), oldType, newType, "type");
                await _dbIntegrator.QueryAsync(logUpdateEvent, null);

                MessageBox.Show("Type updated successfully.");
                await LoadDataGridAsync(productId); // Refresh the DataGridView and reselect the row
            }
            else
            {
                MessageBox.Show("Please select a product.");
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            CalibrateLabels_Click(sender, e);
        }

        private void LoadLabelConfig()
        {
            if (File.Exists(_labelConfigFile))
            {
                string json = File.ReadAllText(_labelConfigFile);
                _labelConfig = JsonConvert.DeserializeObject<LabelConfig>(json);
                LabelWidth.Text = _labelConfig.Width.ToString();
                LabelLength.Text = _labelConfig.Length.ToString();
            }
            else
            {
                _labelConfig = new LabelConfig();
            }
        }


        private void SaveLabelConfig()
        {
            var config = new LabelConfig
            {
                Width = int.Parse(LabelWidth.Text),
                Length = int.Parse(LabelLength.Text)
            };
            File.WriteAllText(_labelConfigFile, JsonConvert.SerializeObject(config));
        }

        private void CalibrateLabels_Click(object sender, EventArgs e)
        {
            int width = int.Parse(LabelWidth.Text);
            int length = int.Parse(LabelLength.Text);

            // Show print dialog
            PrintDocument printDoc = new PrintDocument();
            printDoc.DefaultPageSettings.PaperSize = new PaperSize("Legal", width, length);
            printDoc.PrintPage += (s, ev) => PrintCalibrationPage(ev, width, length);
            PrintDialog printDialog = new PrintDialog();
            printDialog.Document = printDoc;
            if (printDialog.ShowDialog() == DialogResult.OK)
            {
                printDoc.Print();
            }

            // Save the new label configurations
            SaveLabelConfig();
        }

        private void PrintCalibrationPage(PrintPageEventArgs e, int width, int length)
        {
            using (Font font = new Font("Arial", 20))
            {
                // Draw a box around the edge
                e.Graphics.DrawRectangle(Pens.Black, 0, 0, width - 1, length - 1);

                // Draw "Test" in the center
                string text = "Test";
                SizeF textSize = e.Graphics.MeasureString(text, font);
                float x = (width - textSize.Width) / 2;
                float y = (length - textSize.Height) / 2;
                e.Graphics.DrawString(text, font, Brushes.Black, x, y);
            }
        }

        private void Product_List_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                Product_List.ClearSelection();
                Product_List.Rows[e.RowIndex].Selected = true;
                _contextMenu.Show(Cursor.Position);
            }
        }

        private void OpenLogMenuItem_Click(object sender, EventArgs e)
        {
            if (UserSession.Role == "Administrator")
            {
                if (Product_List.SelectedRows.Count > 0)
                {
                    int productId = Convert.ToInt32(Product_List.SelectedRows[0].Cells["id"].Value);
                    TheLog logForm = new TheLog(productId);
                    logForm.Show();
                }
                else
                {
                    MessageBox.Show("Please select a product.");
                }
            }
            else
            {
                MessageBox.Show("You do not have permission to view the log.");
            }
        }

        private void StoreCurrentSearchAndScrollPosition()
        {
            _currentSearchTerm = searchTextBox.Text.Trim();
            _horizontalScrollPosition = Product_List.HorizontalScrollingOffset;

            if (Product_List.FirstDisplayedScrollingRowIndex >= 0)
            {
                _currentScrollRowIndex = Product_List.FirstDisplayedScrollingRowIndex;

            }
            else
            {
                _currentScrollRowIndex = 0;
            }
        }


        private void RestoreSearchAndScrollPosition()
        {
            searchTextBox.Text = _currentSearchTerm;
            if (!string.IsNullOrEmpty(_currentSearchTerm))
            {
                string searchQuery = string.Format(search_function, _currentSearchTerm.ToLower());
                DataTable dataTable = _dbIntegrator.GetDataTableAsync(searchQuery, null).Result;
                Product_List.DataSource = dataTable;
            }

            if (_currentScrollRowIndex >= 0 && _currentScrollRowIndex < Product_List.Rows.Count)
            {
                Product_List.FirstDisplayedScrollingRowIndex = _currentScrollRowIndex;
            }

            if (_horizontalScrollPosition >= 0)
            {
                Product_List.HorizontalScrollingOffset = _horizontalScrollPosition;
            }
        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void CreateQuickSheets_Click(object sender, EventArgs e)
        {
            QuickSheetCreation quickSheetCreationForm = new QuickSheetCreation(_dbIntegrator);
            quickSheetCreationForm.ShowDialog();
        }

        private void loadQuickSheet_Click(object sender, EventArgs e)
        {
            LoadQuickSheetForm loadQuickSheetForm = new LoadQuickSheetForm(_dbIntegrator);
            loadQuickSheetForm.ShowDialog();
        }

        private void button1_Click_1(object sender, EventArgs e)
        {
            DeleteQuickSheetForm deleteQuickSheetForm = new DeleteQuickSheetForm(_dbIntegrator);
            deleteQuickSheetForm.ShowDialog();
        }

        private void UpdateQuickSheet_Click(object sender, EventArgs e)
        {
            using (var updateQuickSheetForm = new UpdateQuickSheetForm(_dbIntegrator))
            {
                updateQuickSheetForm.ShowDialog();
            }
        }

        private void User_Menu_Button_Click(object sender, EventArgs e)
        {
            UserManagement userManagement = new UserManagement(_dbIntegrator);
            userManagement.ShowDialog();
        }

        private void Product_List_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void Begin_Job_Click(object sender, EventArgs e)
        {
            JobCreation jobCreationForm = new JobCreation(_dbIntegrator);
            jobCreationForm.ShowDialog();

        }

        private void button2_Click(object sender, EventArgs e)
        {
            LoadJobs loadJobsForm = new LoadJobs(_dbIntegrator);
            loadJobsForm.Show();
        }

        private async Task LogUserLoginAsync(int userId)
        {
            string eventId = "E007"; // Event ID for user login
            string query = string.Format(insert_log, eventId, userId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            await _dbIntegrator.QueryAsync(query, null);
        }

        private async Task LogLabelPrintingAsync(int productId)
        {
            string eventId = "E006"; // Event ID for label printing
            string query = string.Format(insert_log, eventId, UserSession.UserId, productId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            await _dbIntegrator.QueryAsync(query, null);
        }
        private async Task LogProductUpdateAsync(int productId, string fieldUpdated, string previousValue, string newValue)
        {
            string eventId = "E001"; // Event ID for product updates
            string query = string.Format(insert_log, eventId, UserSession.UserId, productId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), previousValue, newValue, fieldUpdated);
            await _dbIntegrator.QueryAsync(query, null);
        }

        private async void button3_Click(object sender, EventArgs e)
        {
            // OpenFileDialog to select an image file
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp";
                DialogResult result = openFileDialog.ShowDialog();

                if (result == DialogResult.OK)
                {
                    try
                    {
                        // Get the selected file's extension
                        string fileExtension = Path.GetExtension(openFileDialog.FileName);

                        // Ensure the extension is one of the allowed types
                        if (fileExtension == ".jpg" || fileExtension == ".jpeg" || fileExtension == ".png" || fileExtension == ".gif" || fileExtension == ".bmp")
                        {
                            // Define the destination directory and new file name
                            string imageServerPath = ConfigurationManager.AppSettings["ImageServerPath"];
                            string destFileName = $"compico{fileExtension}";
                            string destPath = Path.Combine(imageServerPath, destFileName);

                            // Delete any existing files with the same base name but different extensions
                            foreach (var existingFile in Directory.GetFiles(imageServerPath, "compico.*"))
                            {
                                File.Delete(existingFile);
                            }

                            // Copy the file to the destination directory
                            File.Copy(openFileDialog.FileName, destPath, true);

                            // Optional: You can display a success message or perform additional actions
                            MessageBox.Show("Image uploaded successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show("Invalid file type. Please select a valid image file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Console.WriteLine(ex);
                    }
                }
            }
        }



        public class CompanyInformation
        {
            public string name { get; set; }
        }

        public class LabelConfig
        {
            public int Width { get; set; }
            public int Length { get; set; }
        }

        public class CustomBitmapRenderer : IBarcodeRenderer<Bitmap>
        {

            private string _labelConfigFile = "labelConfig.json"; // File to save label configurations
            public LabelConfig _labelConfig; // Label configuration object

            private void LoadLabelConfig()
            {
                if (File.Exists(_labelConfigFile))
                {
                    string json = File.ReadAllText(_labelConfigFile);
                    _labelConfig = JsonConvert.DeserializeObject<LabelConfig>(json);
                }
                else
                {
                    _labelConfig = new LabelConfig();
                }
            }
            public Bitmap Render(BitMatrix matrix, BarcodeFormat format, string content, EncodingOptions options)
            {
                LoadLabelConfig();
                string[] parts = content.Split('|');
                string alias = parts[0];
                string barcodeText = parts[1];

                int width = _labelConfig.Width;
                int height = _labelConfig.Length;
                int margin = options?.Margin ?? 10;

                Font aliasFont = new Font("Arial", 15);  // Font size 15 for alias
                Font barcodeFont = new Font("Arial", 14);  // Font size 14 for barcode text

                SizeF aliasSize;
                SizeF barcodeSize;

                using (var tempBitmap = new Bitmap(1, 1))
                using (var graphics = Graphics.FromImage(tempBitmap))
                {
                    aliasSize = graphics.MeasureString(alias, aliasFont);
                    barcodeSize = graphics.MeasureString(barcodeText, barcodeFont);
                }

                int totalHeight = height + margin + (int)barcodeSize.Height + (int)aliasSize.Height;
                var bitmap = new Bitmap(width, totalHeight);

                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);

                    graphics.DrawString(alias, aliasFont, Brushes.Black, new PointF((width - aliasSize.Width) / 2, 0));

                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            var color = matrix[x, y] ? Color.Black : Color.White;
                            bitmap.SetPixel(x, y + (int)aliasSize.Height, color);
                        }
                    }

                    graphics.DrawString(barcodeText, barcodeFont, Brushes.Black, new PointF((width - barcodeSize.Width) / 2, height + margin + aliasSize.Height));
                }

                return bitmap;
            }

            public Bitmap Render(BitMatrix matrix, BarcodeFormat format, string content)
            {
                return Render(matrix, format, content, null);
            }


        }


    }
}   