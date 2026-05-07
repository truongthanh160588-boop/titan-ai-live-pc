using System.ComponentModel;
using System.Globalization;
using System.Windows;
using TitanAILivePC.Models;
using TitanAILivePC.Services;

namespace TitanAILivePC;

public partial class PriceCatalogWindow : Window
{
    private readonly List<PriceEditorRow> _rows;
    private bool _isDirty;

    public PriceCatalogWindow()
    {
        InitializeComponent();
        _rows = ProductCatalogService.LoadProductsForEditor()
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Name)
            .Select(p => new PriceEditorRow
            {
                Name = p.Name,
                NormalizedKey = p.NormalizedKey ?? ProductCatalogService.NormalizeProductKey(p.Name),
                Unit = p.Unit,
                Price = p.Price.ToString("0", CultureInfo.InvariantCulture),
                Category = p.Category,
                Aliases = p.Aliases
            })
            .ToList();

        foreach (var row in _rows)
        {
            row.PropertyChanged += OnRowChanged;
        }

        PriceGrid.ItemsSource = _rows;
        Closing += OnWindowClosing;
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PriceEditorRow.Price))
        {
            _isDirty = true;
            StatusText.Text = "Có thay đổi chưa lưu. Đóng cửa sổ sẽ tự lưu.";
            StatusText.Foreground = System.Windows.Media.Brushes.Orange;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveCatalog();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isDirty)
        {
            SaveCatalog();
        }
    }

    private void SaveCatalog()
    {
        try
        {
            var items = new List<ProductItem>();
            foreach (var row in _rows)
            {
                if (!decimal.TryParse(row.Price, NumberStyles.Number, CultureInfo.InvariantCulture, out var priceValue))
                {
                    MessageBox.Show(
                        $"Giá không hợp lệ cho sản phẩm: {row.Name}\nVui lòng nhập số, ví dụ: 4500000",
                        "Lỗi giá",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                items.Add(new ProductItem
                {
                    Category = row.Category,
                    Name = row.Name,
                    NormalizedKey = row.NormalizedKey,
                    Unit = row.Unit,
                    Price = priceValue,
                    Aliases = row.Aliases
                });
            }

            ProductCatalogService.SaveProductsForEditor(items);
            _isDirty = false;
            StatusText.Text = "Đã lưu bảng giá thành công.";
            StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Không thể lưu bảng giá:\n{ex.Message}",
                "Lỗi lưu",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private sealed class PriceEditorRow : INotifyPropertyChanged
    {
        private string _price = string.Empty;

        public string Category { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string NormalizedKey { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public List<string> Aliases { get; init; } = [];

        public string Price
        {
            get => _price;
            set
            {
                if (_price == value)
                {
                    return;
                }

                _price = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Price)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
