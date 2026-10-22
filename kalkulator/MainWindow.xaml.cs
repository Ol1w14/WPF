
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Kalkulator
{
    public partial class MainWindow : Window
    {
        private double firstNumber = 0;
        private string selectedOperation = "";
        private bool startNewNumber = true;

        private readonly CultureInfo culture =
            CultureInfo.GetCultureInfo("pl-PL");

        public MainWindow()
        {
            InitializeComponent();
        }

        // Obsługa cyfr
        private void Number_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            string digit = button.Content.ToString();

            if (startNewNumber || Display.Text == "0")
            {
                Display.Text = digit;
                startNewNumber = false;
            }
            else if (Display.Text.Length < 15)
            {
                Display.Text += digit;
            }
        }

        // Przecinek dziesiętny
        private void Decimal_Click(object sender, RoutedEventArgs e)
        {
            if (startNewNumber)
            {
                Display.Text = "0,";
                startNewNumber = false;
            }
            else if (!Display.Text.Contains(","))
            {
                Display.Text += ",";
            }
        }

        // Wybór działania
        private void Operation_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;

            if (!double.TryParse(
                Display.Text,
                NumberStyles.Any,
                culture,
                out double currentNumber))
            {
                return;
            }

            if (!string.IsNullOrEmpty(selectedOperation)
                && !startNewNumber)
            {
                CalculateResult();

                if (Display.Text == "Błąd")
                    return;

                if (!double.TryParse(
                    Display.Text,
                    NumberStyles.Any,
                    culture,
                    out currentNumber))
                {
                    return;
                }
            }

            firstNumber = currentNumber;
            selectedOperation = button.Tag.ToString();
            startNewNumber = true;
        }

        // Obliczanie wyniku
        private void Equals_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(selectedOperation))
                return;

            CalculateResult();
            selectedOperation = "";
            startNewNumber = true;
        }

        private void CalculateResult()
        {
            if (!double.TryParse(
                Display.Text,
                NumberStyles.Any,
                culture,
                out double secondNumber))
            {
                return;
            }

            double result;

            switch (selectedOperation)
            {
                case "+":
                    result = firstNumber + secondNumber;
                    break;

                case "-":
                    result = firstNumber - secondNumber;
                    break;

                case "*":
                    result = firstNumber * secondNumber;
                    break;

                case "/":
                    if (secondNumber == 0)
                    {
                        ShowError();
                        return;
                    }

                    result = firstNumber / secondNumber;
                    break;

                default:
                    return;
            }

            if (double.IsNaN(result) || double.IsInfinity(result))
            {
                ShowError();
                return;
            }

            Display.Text = result.ToString("G12", culture);
            firstNumber = result;
        }

        // Czyszczenie kalkulatora
        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            Display.Text = "0";
            firstNumber = 0;
            selectedOperation = "";
            startNewNumber = true;
        }

        // Usuwanie ostatniej cyfry
        private void Backspace_Click(object sender, RoutedEventArgs e)
        {
            if (startNewNumber || Display.Text == "Błąd")
                return;

            if (Display.Text.Length > 1)
            {
                Display.Text = Display.Text.Substring(
                    0, Display.Text.Length - 1);
            }
            else
            {
                Display.Text = "0";
                startNewNumber = true;
            }

            if (Display.Text == "-")
            {
                Display.Text = "0";
                startNewNumber = true;
            }
        }

        // Zmiana znaku liczby
        private void Sign_Click(object sender, RoutedEventArgs e)
        {
            if (Display.Text == "Błąd")
                return;

            if (Display.Text == "0")
                return;

            if (Display.Text.StartsWith("-"))
            {
                Display.Text = Display.Text.Substring(1);
            }
            else
            {
                Display.Text = "-" + Display.Text;
            }

            startNewNumber = false;
        }

        // Obsługa błędów
        private void ShowError()
        {
            Display.Text = "Błąd";
            firstNumber = 0;
            selectedOperation = "";
            startNewNumber = true;
            Display.Foreground = Brushes.OrangeRed;
        }
    }
}
