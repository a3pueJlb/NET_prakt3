using System.Globalization;

namespace Lab03_ParcelTracker;

public enum ShipmentStatus
{
    Created,
    InTransit,
    Arrived,
    Received
}

public interface IShipment
{
    string TrackingNumber { get; }
    decimal WeightKg { get; }
    ShipmentStatus Status { get; set; }
}

public class Parcel : IShipment
{
    public string TrackingNumber { get; }
    public string Recipient { get; }
    public decimal WeightKg { get; }
    public ShipmentStatus Status { get; set; }

    public Parcel(string trackingNumber, string recipient, decimal weightKg, ShipmentStatus status = ShipmentStatus.Created)
    {
        if (string.IsNullOrWhiteSpace(trackingNumber))
            throw new ArgumentException("Трек-номер не може бути порожнім.");
        if (string.IsNullOrWhiteSpace(recipient))
            throw new ArgumentException("Одержувач не може бути порожнім.");
        if (weightKg <= 0)
            throw new ArgumentOutOfRangeException(nameof(weightKg), "Вага має бути більшою за 0.");

        TrackingNumber = trackingNumber.Trim().ToUpper();
        Recipient = recipient.Trim();
        WeightKg = weightKg;
        Status = status;
    }
}

public class StatusChangedEventArgs<T> : EventArgs where T : IShipment
{
    public T Shipment { get; }
    public ShipmentStatus OldStatus { get; }
    public ShipmentStatus NewStatus { get; }

    public StatusChangedEventArgs(T shipment, ShipmentStatus oldStatus, ShipmentStatus newStatus)
    {
        Shipment = shipment;
        OldStatus = oldStatus;
        NewStatus = newStatus;
    }
}

public class Tracker<T> where T : IShipment
{
    private readonly List<T> _items = new();
    private Func<T, decimal>? _tariff;

    public event EventHandler<StatusChangedEventArgs<T>>? StatusChanged;

    public int Count => _items.Count;

    public void Add(T item)
    {
        if (_items.Any(x => x.TrackingNumber.Equals(item.TrackingNumber, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Посилка з номером {item.TrackingNumber} вже існує в системі.");
        }
        _items.Add(item);
    }

    public void SetTariff(Func<T, decimal>? tariff) => _tariff = tariff;

    public decimal GetCost(T item) => _tariff?.Invoke(item) ?? (item.WeightKg * 40m);

    public bool AdvanceStatus(string trackingNumber)
    {
        T? item = _items.Find(x => x.TrackingNumber.Equals(trackingNumber.Trim(), StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            Console.WriteLine($"Посилку з номером «{trackingNumber}» не знайдено.");
            return false;
        }

        if (item.Status == ShipmentStatus.Received)
        {
            Console.WriteLine($"Посилка {item.TrackingNumber} вже отримана (фінальний статус Received).");
            return false;
        }

        ShipmentStatus oldStatus = item.Status;
        ShipmentStatus newStatus = (ShipmentStatus)((int)item.Status + 1);
        item.Status = newStatus;

        OnStatusChanged(new StatusChangedEventArgs<T>(item, oldStatus, newStatus));
        return true;
    }

    public void ForEach(Action<T> action)
    {
        foreach (T item in _items)
        {
            action(item);
        }
    }

    protected virtual void OnStatusChanged(StatusChangedEventArgs<T> e)
    {
        StatusChanged?.Invoke(this, e);
    }
}

class Program
{
    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        var tracker = new Tracker<Parcel>();
        var statusHistory = new List<string>();
        string currentTariffName = "Стандартний (40 грн/кг)";

        tracker.Add(new Parcel("UA-1001", "Олена Коваль", 2.0m, ShipmentStatus.Created));
        tracker.Add(new Parcel("UA-1002", "Дмитро Бондар", 5.5m, ShipmentStatus.InTransit));
        tracker.Add(new Parcel("UA-1003", "Максим Сидоренко", 1.2m, ShipmentStatus.Arrived));
        tracker.Add(new Parcel("UA-1004", "Ірина Мельник", 3.0m, ShipmentStatus.Received));

        tracker.StatusChanged += SendSmsNotification;
        tracker.StatusChanged += (sender, e) =>
        {
            string record = $"[{DateTime.Now:HH:mm:ss}] {e.Shipment.TrackingNumber} ({e.Shipment.Recipient}): {e.OldStatus} -> {e.NewStatus}";
            statusHistory.Add(record);
        };

        var tariffs = new Dictionary<int, (string Name, Func<Parcel, decimal>? Rule)>
        {
            [1] = ("Стандартний (40 грн/кг)", null),
            [2] = ("Фіксований (70 грн)", p => 70m),
            [3] = ("Експрес (100 + 60 грн/кг)", p => 100m + p.WeightKg * 60m),
            [4] = ("Акційний (-20% від стандарту)", p => p.WeightKg * 40m * 0.8m)
        };

        bool isRunning = true;
        while (isRunning)
        {
            Console.WriteLine("\n================ Служба відстеження посилок ================");
            Console.WriteLine($"Поточний тариф: {currentTariffName}");
            Console.WriteLine("1. Вихід");
            Console.WriteLine("2. Додати посилку");
            Console.WriteLine("3. Показати всі посилки");
            Console.WriteLine("4. Оновити статус посилки");
            Console.WriteLine("5. Обрати тариф розрахунку");
            Console.WriteLine("6. Показати історію змін статусів");
            Console.WriteLine("============================================================");

            int choice = ReadInt("Оберіть пункт меню (1-6): ", 1, 6);

            isRunning = choice switch
            {
                1 => false,
                2 => HandleAddParcel(tracker),
                3 => HandleShowParcels(tracker, currentTariffName),
                4 => HandleAdvanceStatus(tracker),
                5 => HandleChooseTariff(tracker, tariffs, ref currentTariffName),
                6 => HandleShowHistory(statusHistory),
                _ => true
            };
        }

        Console.WriteLine("\nРоботу програми завершено.");
    }

    static void SendSmsNotification(object? sender, StatusChangedEventArgs<Parcel> e)
    {
        string statusText = e.NewStatus switch
        {
            ShipmentStatus.InTransit => "передана у доставку (у дорозі)",
            ShipmentStatus.Arrived => "прибула у відділення",
            ShipmentStatus.Received => "успішно отримана",
            _ => e.NewStatus.ToString()
        };
        Console.WriteLine($"[SMS для {e.Shipment.Recipient}]: посилка {e.Shipment.TrackingNumber} {statusText}.");
    }

    static bool HandleAddParcel(Tracker<Parcel> tracker)
    {
        string number = ReadNonEmpty("Введіть трек-номер (напр. UA-1005): ");
        string recipient = ReadNonEmpty("Введіть одержувача: ");
        decimal weight = ReadPositiveDecimal("Введіть вагу посилки (кг): ");

        try
        {
            tracker.Add(new Parcel(number, recipient, weight));
            Console.WriteLine($"Посилку {number.ToUpper()} успішно зареєстровано.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Помилка: {ex.Message}");
        }
        return true;
    }

    static bool HandleShowParcels(Tracker<Parcel> tracker, string tariffName)
    {
        if (tracker.Count == 0)
        {
            Console.WriteLine("Список посилок порожній.");
            return true;
        }

        Console.WriteLine($"\n=== Список посилок (Тариф: {tariffName}) ===");
        tracker.ForEach(p =>
        {
            decimal cost = tracker.GetCost(p);
            Console.WriteLine($"{p.TrackingNumber,-10} | Одержувач: {p.Recipient,-18} | Статус: {p.Status,-10} | Вага: {p.WeightKg,5:F2} кг | Вартість: {cost,7:F2} грн");
        });
        return true;
    }

    static bool HandleAdvanceStatus(Tracker<Parcel> tracker)
    {
        string number = ReadNonEmpty("Введіть трек-номер посилки для оновлення статусу: ");
        tracker.AdvanceStatus(number);
        return true;
    }

    static bool HandleChooseTariff(
        Tracker<Parcel> tracker,
        Dictionary<int, (string Name, Func<Parcel, decimal>? Rule)> tariffs,
        ref string currentTariffName)
    {
        Console.WriteLine("\n--- Оберіть тариф ---");
        Console.WriteLine("1. Стандартний (40 грн/кг)");
        Console.WriteLine("2. Фіксований (70 грн)");
        Console.WriteLine("3. Експрес (100 + 60 грн/кг)");
        Console.WriteLine("4. Акційний (-20% від стандарту)");

        int tChoice = ReadInt("Ваш вибір (1-4): ", 1, 4);
        var selected = tariffs[tChoice];
        tracker.SetTariff(selected.Rule);
        currentTariffName = selected.Name;
        Console.WriteLine($"Встановлено тариф: {currentTariffName}");
        return true;
    }

    static bool HandleShowHistory(List<string> history)
    {
        if (history.Count == 0)
        {
            Console.WriteLine("Історія змін за поточний сеанс порожня.");
            return true;
        }

        Console.WriteLine("\n=== Історія змін статусів ===");
        foreach (string item in history)
        {
            Console.WriteLine(item);
        }
        return true;
    }

    static string ReadNonEmpty(string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            string? input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input)) return input.Trim();
            Console.WriteLine("Помилка: значення не може бути порожнім.");
        }
    }

    static int ReadInt(string prompt, int min, int max)
    {
        while (true)
        {
            Console.Write(prompt);
            if (int.TryParse(Console.ReadLine(), out int val) && val >= min && val <= max) return val;
            Console.WriteLine($"Введіть ціле число від {min} до {max}.");
        }
    }

    static decimal ReadPositiveDecimal(string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            string normalized = (Console.ReadLine() ?? "").Trim().Replace(',', '.');
            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal val) && val > 0)
                return val;
            Console.WriteLine("Помилка: введіть додатне число.");
        }
    }
}