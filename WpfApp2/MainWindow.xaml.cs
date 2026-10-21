using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace MoneyManager
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<Transaction> transactions = new(); private readonly ObservableCollection<BudgetEntry> budgets = new();

        private ICollectionView? transactionsView;
        private Transaction? editingTransaction;

        private bool isInitializing = true;

        private readonly string dataFolder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "MoneyManager");

        private string DataFile =>
            Path.Combine(dataFolder, "transactions.json");

        private string BudgetFile =>
            Path.Combine(dataFolder, "budgets.json");

        private static readonly CultureInfo PolishCulture =
            CultureInfo.GetCultureInfo("pl-PL");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public MainWindow()
        {
            InitializeComponent();

            transactionsView = CollectionViewSource.GetDefaultView(transactions);
            transactionsView.Filter = FilterTransaction;

            TransactionsGrid.ItemsSource = transactionsView;
            BudgetsGrid.ItemsSource = budgets;

            LoadTransactions();
            LoadBudgets();

            PopulateMonthFilters();
            PopulateReportMonths();

            isInitializing = false;

            UpdateAll();
        }

        // =========================================================
        // TRANSAKCJE - DODAWANIE
        // =========================================================

        private void AddSelectedTransaction_Click(
            object sender, RoutedEventArgs e)
        {
            string type = GetComboContent(TypeCombo, "Wydatek");

            AddTransaction(type);
        }

        private void AddIncome_Click(object sender, RoutedEventArgs e)
        {
            AddTransaction("Przychód");
        }

        private void AddExpense_Click(object sender, RoutedEventArgs e)
        {
            AddTransaction("Wydatek");
        }

        private void AddTransaction(string type)
        {
            if (type != "Przychód" && type != "Wydatek")
                type = "Wydatek";

            if (!TryReadAmount(AmountBox.Text, out decimal amount))
            {
                ShowWarning(
                    "Wpisz prawidłową kwotę, np. 125,50.",
                    "Nieprawidłowa kwota");
                return;
            }

            if (amount <= 0)
            {
                ShowWarning(
                    "Kwota musi być większa od zera.",
                    "Nieprawidłowa kwota");
                return;
            }

            if (amount > 1_000_000_000m)
            {
                ShowWarning(
                    "Maksymalna kwota to 1 000 000 000 zł.",
                    "Kwota jest zbyt duża");
                return;
            }

            string category = GetComboContent(CategoryCombo, "Inne");
            string description = DescriptionBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(description))
                description = category;

            if (editingTransaction != null)
            {
                editingTransaction.Type = type;
                editingTransaction.Category = category;
                editingTransaction.Description = description;
                editingTransaction.Amount = amount;

                // Powiadamia widok tabeli o zmianie danych.
                transactionsView?.Refresh();

                Transaction edited = editingTransaction;

                CancelEditMode();

                SaveTransactions();
                UpdateAll();

                TransactionsGrid.SelectedItem = edited;
                TransactionsGrid.ScrollIntoView(edited);

                return;
            }

            var transaction = new Transaction
            {
                Date = DateTime.Now,
                Type = type,
                Category = category,
                Description = description,
                Amount = amount
            };

            transactions.Insert(0, transaction);

            SaveTransactions();

            AmountBox.Text = "10,00";
            DescriptionBox.Clear();

            RefreshMonthFilters();
            UpdateAll();

            TransactionsGrid.SelectedItem = transaction;
            TransactionsGrid.ScrollIntoView(transaction);
        }

        // =========================================================
        // EDYCJA TRANSAKCJI
        // =========================================================

        private void EditTransaction_Click(
            object sender, RoutedEventArgs e)
        {
            if (TransactionsGrid.SelectedItem is not Transaction selected)
            {
                ShowWarning(
                    "Najpierw zaznacz transakcję w tabeli.",
                    "Brak zaznaczenia");
                return;
            }

            editingTransaction = selected;

            SelectComboItem(TypeCombo, selected.Type);
            SelectComboItem(CategoryCombo, selected.Category);

            AmountBox.Text =
                selected.Amount.ToString("0.00", PolishCulture);

            DescriptionBox.Text = selected.Description;

            TransactionFormTitle.Text = "Edytuj transakcję";

            SaveTransactionButton.Content = "Zapisz zmiany";
            CancelEditButton.Visibility = Visibility.Visible;

            TransactionsGrid.ScrollIntoView(selected);
            AmountBox.Focus();
            AmountBox.SelectAll();
        }

        private void CancelEdit_Click(
            object sender, RoutedEventArgs e)
        {
            CancelEditMode();
        }

        private void CancelEditMode()
        {
            editingTransaction = null;

            TransactionFormTitle.Text = "Nowa transakcja";
            SaveTransactionButton.Content = "Dodaj transakcję";

            CancelEditButton.Visibility = Visibility.Collapsed;

            SelectComboItem(TypeCombo, "Wydatek");
            SelectComboItem(CategoryCombo, "Inne");

            AmountBox.Text = "10,00";
            DescriptionBox.Clear();
        }

        // =========================================================
        // ZMIANA KWOTY
        // =========================================================

        private void IncreaseAmount_Click(
            object sender, RoutedEventArgs e)
        {
            ChangeAmount(1m);
        }

        private void DecreaseAmount_Click(
            object sender, RoutedEventArgs e)
        {
            ChangeAmount(-1m);
        }

        private void ChangeAmount(decimal change)
        {
            if (!TryReadAmount(AmountBox.Text, out decimal amount))
                amount = 0m;

            amount = Math.Clamp(amount + change, 0m, 1_000_000_000m);

            AmountBox.Text = amount.ToString("0.00", PolishCulture);
            AmountBox.CaretIndex = AmountBox.Text.Length;
        }

        // =========================================================
        // USUWANIE TRANSAKCJI
        // =========================================================

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (TransactionsGrid.SelectedItem is not Transaction selected)
            {
                ShowWarning(
                    "Zaznacz transakcję, którą chcesz usunąć.",
                    "Brak zaznaczenia");
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                $"Czy na pewno usunąć transakcję „{selected.Description}” " +
                $"na kwotę {selected.Amount.ToString("N2", PolishCulture)} zł?",
                "Potwierdzenie usunięcia",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            if (editingTransaction == selected)
                CancelEditMode();

            transactions.Remove(selected);

            SaveTransactions();
            RefreshMonthFilters();
            UpdateAll();
        }

        // =========================================================
        // WYSZUKIWANIE I FILTROWANIE
        // =========================================================

        private bool FilterTransaction(object item)
        {
            if (item is not Transaction transaction)
                return false;

            string search = SearchBox?.Text.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(search))
            {
                bool matches =
                    transaction.Description.Contains(
                        search, StringComparison.OrdinalIgnoreCase)
                    || transaction.Category.Contains(
                        search, StringComparison.OrdinalIgnoreCase)
                    || transaction.Type.Contains(
                        search, StringComparison.OrdinalIgnoreCase)
                    || transaction.Amount.ToString(
                        "0.00", PolishCulture).Contains(
                        search, StringComparison.OrdinalIgnoreCase)
                    || transaction.Amount.ToString(
                        CultureInfo.InvariantCulture).Contains(
                        search, StringComparison.OrdinalIgnoreCase);

                if (!matches)
                    return false;
            }

            string typeFilter = GetComboContent(
                TypeFilterCombo, "Wszystkie");

            if (typeFilter != "Wszystkie" &&
                transaction.Type != typeFilter)
                return false;

            string categoryFilter = GetComboContent(
                CategoryFilterCombo, "Wszystkie");

            if (categoryFilter != "Wszystkie" &&
                transaction.Category != categoryFilter)
                return false;

            string? monthKey = GetSelectedMonthKey(MonthFilterCombo);

            if (monthKey != null &&
                transaction.Date.ToString("yyyy-MM") != monthKey)
                return false;

            return true;
        }

        private void SearchBox_TextChanged(
            object sender, TextChangedEventArgs e)
        {
            if (!isInitializing)
                RefreshTransactionView();
        }

        private void Filter_Changed(
            object sender, SelectionChangedEventArgs e)
        {
            if (!isInitializing)
                RefreshTransactionView();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshTransactionView();
            UpdateAll();
        }

        private void ClearFilters_Click(
            object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();

            SelectComboItem(TypeFilterCombo, "Wszystkie");
            SelectComboItem(CategoryFilterCombo, "Wszystkie");

            if (MonthFilterCombo.Items.Count > 0)
                MonthFilterCombo.SelectedIndex = 0;

            RefreshTransactionView();
        }

        private void RefreshTransactionView()
        {
            transactionsView?.Refresh();

            if (FilterResultText != null && transactionsView != null)
            {
                FilterResultText.Text =
                    $"Wyświetlane transakcje: {transactionsView.Cast<object>().Count()} " +
                    $"z {transactions.Count}";
            }
        }

        // =========================================================
        // DASHBOARD I STATYSTYKI
        // =========================================================

        private void UpdateAll()
        {
            UpdateStatistics();
            UpdateMonthlyChart();
            UpdateCategoryChart();
            UpdateMonthlyReport();
            UpdateBudgets();
            RefreshTransactionView();
        }

        private void UpdateStatistics()
        {
            decimal income = transactions
                .Where(t => t.Type == "Przychód")
                .Sum(t => t.Amount);

            decimal expenses = transactions
                .Where(t => t.Type == "Wydatek")
                .Sum(t => t.Amount);

            decimal balance = income - expenses;

            BalanceText.Text = FormatMoney(balance);
            TotalIncomeText.Text = FormatMoney(income);
            TotalExpenseText.Text = FormatMoney(expenses);

            BalanceText.Foreground = new SolidColorBrush(
                balance < 0
                    ? Color.FromRgb(251, 113, 133)
                    : Color.FromRgb(248, 250, 252));

            DateTime now = DateTime.Now;

            var currentMonth = transactions
                .Where(t => t.Date.Year == now.Year &&
                            t.Date.Month == now.Month)
                .ToList();

            decimal monthIncome = currentMonth
                .Where(t => t.Type == "Przychód")
                .Sum(t => t.Amount);

            decimal monthExpenses = currentMonth
                .Where(t => t.Type == "Wydatek")
                .Sum(t => t.Amount);

            MonthIncomeText.Text = FormatMoney(monthIncome);
            MonthExpenseText.Text = FormatMoney(monthExpenses);

            TransactionCountText.Text = transactions.Count.ToString(
                PolishCulture);
        }

        // =========================================================
        // WYKRES: OSTATNIE 6 MIESIĘCY
        // =========================================================

        private void UpdateMonthlyChart()
        {
            var result = new List<MonthlyChartItem>();

            for (int i = 5; i >= 0; i--)
            {
                DateTime month = new DateTime(
                    DateTime.Now.Year,
                    DateTime.Now.Month,
                    1).AddMonths(-i);

                var monthTransactions = transactions
                    .Where(t => t.Date.Year == month.Year &&
                                t.Date.Month == month.Month)
                    .ToList();

                decimal income = monthTransactions
                    .Where(t => t.Type == "Przychód")
                    .Sum(t => t.Amount);

                decimal expenses = monthTransactions
                    .Where(t => t.Type == "Wydatek")
                    .Sum(t => t.Amount);

                result.Add(new MonthlyChartItem
                {
                    Label = month.ToString("MMM yy", PolishCulture),
                    Income = income,
                    Expenses = expenses
                });
            }

            decimal max = result
                .SelectMany(m => new[] { m.Income, m.Expenses })
                .DefaultIfEmpty(0m)
                .Max();

            foreach (MonthlyChartItem item in result)
            {
                item.IncomePercent = max > 0
                    ? (double)(item.Income / max * 100m)
                    : 0;

                item.ExpensePercent = max > 0
                    ? (double)(item.Expenses / max * 100m)
                    : 0;
            }

            MonthlyChart.ItemsSource = result;
        }

        // =========================================================
        // RAPORT: WYDATKI WEDŁUG KATEGORII
        // =========================================================

        private void UpdateCategoryChart()
        {
            DateTime month = GetSelectedReportMonth();

            var categories = transactions
                .Where(t => t.Type == "Wydatek" &&
                            t.Date.Year == month.Year &&
                            t.Date.Month == month.Month)
                .GroupBy(t => t.Category)
                .Select(g => new CategoryChartItem
                {
                    Category = g.Key,
                    Amount = g.Sum(t => t.Amount)
                })
                .OrderByDescending(x => x.Amount)
                .ToList();

            decimal max = categories
                .Select(c => c.Amount)
                .DefaultIfEmpty(0m)
                .Max();

            foreach (CategoryChartItem category in categories)
            {
                category.Percent = max > 0
                    ? (double)(category.Amount / max * 100m)
                    : 0;
            }

            CategoryChart.ItemsSource = categories;

            NoCategoryText.Visibility = categories.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // =========================================================
        // RAPORT MIESIĘCZNY
        // =========================================================

        private void ReportMonth_Changed(
            object sender, SelectionChangedEventArgs e)
        {
            if (isInitializing || ReportMonthCombo == null)
                return;

            UpdateMonthlyReport();
            UpdateCategoryChart();
        }

        private void UpdateMonthlyReport()
        {
            DateTime month = GetSelectedReportMonth();

            var monthTransactions = transactions
                .Where(t => t.Date.Year == month.Year &&
                            t.Date.Month == month.Month)
                .ToList();

            decimal income = monthTransactions
                .Where(t => t.Type == "Przychód")
                .Sum(t => t.Amount);

            decimal expenses = monthTransactions
                .Where(t => t.Type == "Wydatek")
                .Sum(t => t.Amount);

            decimal balance = income - expenses;
            decimal total = income + expenses;

            ReportIncomeText.Text = FormatMoney(income);
            ReportExpenseText.Text = FormatMoney(expenses);
            ReportBalanceText.Text = FormatMoney(balance);

            ReportBalanceText.Foreground = new SolidColorBrush(
                balance < 0
                    ? Color.FromRgb(251, 113, 133)
                    : Color.FromRgb(74, 222, 128));

            ReportCountText.Text =
                monthTransactions.Count.ToString(PolishCulture);

            ReportIncomeBar.Value = total > 0
                ? (double)(income / total * 100m)
                : 0;

            ReportExpenseBar.Value = total > 0
                ? (double)(expenses / total * 100m)
                : 0;
        }

        private DateTime GetSelectedReportMonth()
        {
            string? key = GetSelectedMonthKey(ReportMonthCombo);

            if (key != null &&
                DateTime.TryParseExact(
                    key,
                    "yyyy-MM",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime selected))
            {
                return new DateTime(selected.Year, selected.Month, 1);
            }

            DateTime now = DateTime.Now;
            return new DateTime(now.Year, now.Month, 1);
        }

        // =========================================================
        // BUDŻETY I LIMITY
        // =========================================================

        private void AddBudget_Click(
            object sender, RoutedEventArgs e)
        {
            string category = GetComboContent(
                BudgetCategoryCombo, "Inne");

            if (!TryReadAmount(BudgetLimitBox.Text, out decimal limit) ||
                limit <= 0)
            {
                ShowWarning(
                    "Wpisz prawidłowy limit większy od zera.",
                    "Nieprawidłowy limit");
                return;
            }

            if (limit > 1_000_000_000m)
            {
                ShowWarning(
                    "Maksymalny limit to 1 000 000 000 zł.",
                    "Limit jest zbyt duży");
                return;
            }

            BudgetEntry? existing = budgets.FirstOrDefault(
                b => b.Category == category);

            if (existing != null)
            {
                existing.MonthlyLimit = limit;
            }
            else
            {
                budgets.Add(new BudgetEntry
                {
                    Category = category,
                    MonthlyLimit = limit
                });
            }

            BudgetLimitBox.Clear();

            SaveBudgets();
            UpdateBudgets();
        }

        private void DeleteBudget_Click(
            object sender, RoutedEventArgs e)
        {
            if (BudgetsGrid.SelectedItem is not BudgetEntry selected)
            {
                ShowWarning(
                    "Zaznacz budżet, który chcesz usunąć.",
                    "Brak zaznaczenia");
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                $"Czy na pewno usunąć limit dla kategorii „{selected.Category}”?",
                "Potwierdzenie usunięcia",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            budgets.Remove(selected);

            SaveBudgets();
            UpdateBudgets();
        }

        private void UpdateBudgets()
        {
            DateTime now = DateTime.Now;

            foreach (BudgetEntry budget in budgets)
            {
                budget.Spent = transactions
                    .Where(t => t.Type == "Wydatek" &&
                                t.Category == budget.Category &&
                                t.Date.Year == now.Year &&
                                t.Date.Month == now.Month)
                    .Sum(t => t.Amount);

                budget.Percent = budget.MonthlyLimit > 0
                    ? Math.Min(
                        100,
                        (double)(budget.Spent /
                                 budget.MonthlyLimit * 100m))
                    : 0;

                if (budget.Spent > budget.MonthlyLimit)
                {
                    budget.Status = "Przekroczono limit";
                }
                else if (budget.Spent >= budget.MonthlyLimit * 0.8m)
                {
                    budget.Status = "Blisko limitu";
                }
                else
                {
                    budget.Status = "W normie";
                }
            }

            BudgetsGrid.Items.Refresh();

            decimal totalLimit = budgets.Sum(b => b.MonthlyLimit);
            decimal totalSpent = budgets.Sum(b => b.Spent);

            BudgetSummaryText.Text =
                $"Wydano {FormatMoney(totalSpent)} z {FormatMoney(totalLimit)}";

            BudgetSummaryBar.Value = totalLimit > 0
                ? Math.Min(100, (double)(totalSpent / totalLimit * 100m))
                : 0;

            BudgetSummaryBar.Foreground = totalLimit > 0 &&
                                           totalSpent > totalLimit
                ? new SolidColorBrush(Color.FromRgb(251, 113, 133))
                : new SolidColorBrush(Color.FromRgb(74, 222, 128));
        }

        // =========================================================
        // LISTY MIESIĘCY
        // =========================================================

        private void PopulateMonthFilters()
        {
            MonthFilterCombo.Items.Clear();

            MonthFilterCombo.Items.Add(new ComboBoxItem
            {
                Content = "Wszystkie",
                Tag = null
            });

            DateTime currentMonth = new(
                DateTime.Now.Year,
                DateTime.Now.Month,
                1);

            for (int i = 0; i < 36; i++)
            {
                DateTime month = currentMonth.AddMonths(-i);

                MonthFilterCombo.Items.Add(new ComboBoxItem
                {
                    Content = month.ToString("MM.yyyy", PolishCulture),
                    Tag = month.ToString("yyyy-MM")
                });
            }

            MonthFilterCombo.SelectedIndex = 0;
        }

        private void PopulateReportMonths()
        {
            ReportMonthCombo.Items.Clear();

            DateTime currentMonth = new(
                DateTime.Now.Year,
                DateTime.Now.Month,
                1);

            for (int i = 0; i < 24; i++)
            {
                DateTime month = currentMonth.AddMonths(-i);

                ReportMonthCombo.Items.Add(new ComboBoxItem
                {
                    Content = month.ToString("MMMM yyyy", PolishCulture),
                    Tag = month.ToString("yyyy-MM")
                });
            }

            ReportMonthCombo.SelectedIndex = 0;
        }

        private void RefreshMonthFilters()
        {
            string? selectedMonth = GetSelectedMonthKey(MonthFilterCombo);
            string? selectedReport = GetSelectedMonthKey(ReportMonthCombo);

            PopulateMonthFilters();
            PopulateReportMonths();

            if (selectedMonth != null)
                SelectComboTag(MonthFilterCombo, selectedMonth);

            if (selectedReport != null)
                SelectComboTag(ReportMonthCombo, selectedReport);
        }

        // =========================================================
        // ZAPIS I ODCZYT TRANSAKCJI
        // =========================================================

        private void SaveTransactions()
        {
            SaveJson(DataFile, transactions.ToArray(), "zapis transakcji");
        }

        private void LoadTransactions()
        {
            Transaction[]? loaded =
                LoadJson<Transaction[]>(DataFile, "odczyt transakcji");

            if (loaded == null)
                return;

            foreach (Transaction? transaction in loaded)
            {
                if (transaction == null ||
                    transaction.Amount <= 0 ||
                    transaction.Amount > 1_000_000_000m ||
                    (transaction.Type != "Przychód" &&
                     transaction.Type != "Wydatek"))
                {
                    continue;
                }

                transaction.Category ??= "Inne";
                transaction.Description ??= transaction.Category;

                transactions.Add(transaction);
            }
        }

        // =========================================================
        // ZAPIS I ODCZYT BUDŻETÓW
        // =========================================================

        private void SaveBudgets()
        {
            BudgetData[] data = budgets
                .Select(b => new BudgetData
                {
                    Category = b.Category,
                    MonthlyLimit = b.MonthlyLimit
                })
                .ToArray();

            SaveJson(BudgetFile, data, "zapis budżetów");
        }

        private void LoadBudgets()
        {
            BudgetData[]? loaded =
                LoadJson<BudgetData[]>(BudgetFile, "odczyt budżetów");

            if (loaded == null)
                return;

            foreach (BudgetData? item in loaded)
            {
                if (item == null ||
                    string.IsNullOrWhiteSpace(item.Category) ||
                    item.MonthlyLimit <= 0 ||
                    item.MonthlyLimit > 1_000_000_000m)
                {
                    continue;
                }

                if (budgets.Any(b => b.Category == item.Category))
                    continue;

                budgets.Add(new BudgetEntry
                {
                    Category = item.Category,
                    MonthlyLimit = item.MonthlyLimit
                });
            }
        }

        private void SaveJson<T>(string path, T data, string operation)
        {
            string temporaryFile = path + ".tmp";

            try
            {
                Directory.CreateDirectory(dataFolder);

                string json = JsonSerializer.Serialize(data, JsonOptions);

                File.WriteAllText(temporaryFile, json);

                File.Move(temporaryFile, path, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Nie udało się wykonać operacji: {operation}.\n\n{ex.Message}",
                    "Błąd zapisu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryFile))
                        File.Delete(temporaryFile);
                }
                catch
                {
                    // Plik tymczasowy może pozostać, jeśli jest zablokowany.
                }
            }
        }

        private T? LoadJson<T>(string path, string operation)
        {
            try
            {
                if (!File.Exists(path))
                    return default;

                string json = File.ReadAllText(path);

                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Nie udało się wykonać operacji: {operation}.\n\n{ex.Message}",
                    "Błąd odczytu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return default;
            }
        }

        // =========================================================
        // FUNKCJE POMOCNICZE
        // =========================================================

        private static bool TryReadAmount(
            string? text, out decimal amount)
        {
            string value = (text ?? "")
                .Trim()
                .Replace(" ", "")
                .Replace("\u00A0", "")
                .Replace(',', '.');

            return decimal.TryParse(
                value,
                NumberStyles.AllowDecimalPoint |
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out amount);
        }

        private static string FormatMoney(decimal amount)
        {
            return amount.ToString("N2", PolishCulture) + " zł";
        }

        private static string GetComboContent(
            ComboBox? combo, string fallback)
        {
            if (combo?.SelectedItem is ComboBoxItem item)
                return item.Content?.ToString() ?? fallback;

            return fallback;
        }

        private static string? GetSelectedMonthKey(ComboBox? combo)
        {
            if (combo?.SelectedItem is ComboBoxItem item)
                return item.Tag as string;

            return null;
        }

        private static void SelectComboItem(
            ComboBox combo, string value)
        {
            foreach (object item in combo.Items)
            {
                if (item is ComboBoxItem comboItem &&
                    string.Equals(
                        comboItem.Content?.ToString(),
                        value,
                        StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = comboItem;
                    return;
                }
            }

            if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;
        }

        private static void SelectComboTag(
            ComboBox combo, string tag)
        {
            foreach (object item in combo.Items)
            {
                if (item is ComboBoxItem comboItem &&
                    string.Equals(
                        comboItem.Tag as string,
                        tag,
                        StringComparison.Ordinal))
                {
                    combo.SelectedItem = comboItem;
                    return;
                }
            }
        }

        private static void ShowWarning(string message, string title)
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    // =============================================================
    // MODEL TRANSAKCJI
    // =============================================================

    public class Transaction : INotifyPropertyChanged
    {
        private DateTime date = DateTime.Now;
        private string type = "Wydatek";
        private string category = "Inne";
        private string description = "";
        private decimal amount;

        public DateTime Date
        {
            get => date;
            set
            {
                date = value;
                OnPropertyChanged(nameof(Date));
                OnPropertyChanged(nameof(DateDisplay));
            }
        }

        public string Type
        {
            get => type;
            set
            {
                type = value;
                OnPropertyChanged(nameof(Type));
                OnPropertyChanged(nameof(AmountDisplay));
            }
        }

        public string Category
        {
            get => category;
            set
            {
                category = value;
                OnPropertyChanged(nameof(Category));
            }
        }

        public string Description
        {
            get => description;
            set
            {
                description = value;
                OnPropertyChanged(nameof(Description));
            }
        }

        public decimal Amount
        {
            get => amount;
            set
            {
                amount = value;
                OnPropertyChanged(nameof(Amount));
                OnPropertyChanged(nameof(AmountDisplay));
            }
        }

        [JsonIgnore]
        public string DateDisplay =>
            Date.ToString("dd.MM.yyyy HH:mm", PolishCulture);

        [JsonIgnore]
        public string AmountDisplay =>
            (Type == "Przychód" ? "+" : "−") +
            Amount.ToString("N2", PolishCulture) + " zł";

        private static readonly CultureInfo PolishCulture =
            CultureInfo.GetCultureInfo("pl-PL");

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }
    }

    // =============================================================
    // MODEL BUDŻETU
    // =============================================================

    public class BudgetEntry : INotifyPropertyChanged
    {
        private string category = "Inne";
        private decimal monthlyLimit;
        private decimal spent;
        private double percent;
        private string status = "W normie";

        public string Category
        {
            get => category;
            set
            {
                category = value;
                OnPropertyChanged(nameof(Category));
            }
        }

        public decimal MonthlyLimit
        {
            get => monthlyLimit;
            set
            {
                monthlyLimit = value;
                OnPropertyChanged(nameof(MonthlyLimit));
            }
        }

        [JsonIgnore]
        public decimal Spent
        {
            get => spent;
            set
            {
                spent = value;
                OnPropertyChanged(nameof(Spent));
            }
        }

        [JsonIgnore]
        public double Percent
        {
            get => percent;
            set
            {
                percent = value;
                OnPropertyChanged(nameof(Percent));
            }
        }

        [JsonIgnore]
        public string Status
        {
            get => status;
            set
            {
                status = value;
                OnPropertyChanged(nameof(Status));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }
    }

    // Model do trwałego zapisu limitów.
    public class BudgetData
    {
        public string Category { get; set; } = "Inne";
        public decimal MonthlyLimit { get; set; }
    }

    // =============================================================
    // MODELE WYKRESÓW I RAPORTÓW
    // =============================================================

    public class MonthlyChartItem
    {
        public string Label { get; set; } = "";
        public decimal Income { get; set; }
        public decimal Expenses { get; set; }
        public double IncomePercent { get; set; }
        public double ExpensePercent { get; set; }
    }

    public class CategoryChartItem
    {
        public string Category { get; set; } = "";
        public decimal Amount { get; set; }
        public double Percent { get; set; }
    }


}