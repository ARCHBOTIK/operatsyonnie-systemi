using System.Text;

namespace SecurePassword;

public class CardEntry : IHasID
{
    public int Id { get; set; }
    public byte[] TitleBytes { get; set; }
    public byte[] CardNumberBytes { get; set; }
    public byte[] CardHolderBytes { get; set; }
    public byte[] ExpiryDateBytes { get; set; }
    public byte[] CvvBytes { get; set; }
    public byte[] BankNameBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string Title
    {
        get => TitleBytes != null ? Encoding.UTF8.GetString(TitleBytes) : string.Empty;
        set => TitleBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
    }

    public string CardNumber
    {
        get => CardNumberBytes != null ? Encoding.UTF8.GetString(CardNumberBytes) : string.Empty;
        set => CardNumberBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
    }

    public string CardHolder
    {
        get => CardHolderBytes != null ? Encoding.UTF8.GetString(CardHolderBytes) : string.Empty;
        set => CardHolderBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
    }

    public string ExpiryDate
    {
        get => ExpiryDateBytes != null ? Encoding.UTF8.GetString(ExpiryDateBytes) : string.Empty;
        set => ExpiryDateBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
    }

    public string Cvv
    {
        get => CvvBytes != null ? Encoding.UTF8.GetString(CvvBytes) : string.Empty;
        set => CvvBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
    }

    public string BankName
    {
        get => BankNameBytes != null ? Encoding.UTF8.GetString(BankNameBytes) : string.Empty;
        set => BankNameBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
    }

    public CardEntry()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}