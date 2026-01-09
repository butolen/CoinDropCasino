using System;

namespace WebApp.services.dtos;

public enum TransactionActionFilter
{
    All = 0,
    Deposit = 1,
    Withdrawal = 2
}

public enum TransactionTypeFilter
{
    All = 0,
    Physical = 1,
    Crypto = 2
}
public sealed class TransactionHistoryQuery
{
    public TransactionActionFilter Action { get; set; } = TransactionActionFilter.All;
    public TransactionTypeFilter Type { get; set; } = TransactionTypeFilter.All;

    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }

    // ✅ NEU: Mindestbetrag für Deposits in EUR
    public double? MinDepositEur { get; set; }
}

public sealed class TransactionHistoryRowDto
{
    public string Action { get; set; } = string.Empty;     // "Deposit" | "Withdrawal"
    public string Type { get; set; } = string.Empty;       // "Physical" | "Crypto"
    public double EurAmount { get; set; }                  // double wie gewünscht
    public string AssetType { get; set; } = string.Empty;  // "Cash" bei Physical, sonst z.B. "SOL"
    public string Network { get; set; } = string.Empty;    // leer bei Physical
    public DateTime TimestampUtc { get; set; }
    
    public string? TxHash { get; set; }

}

