namespace SecurePassword;

public class CardInfo : IHasID
{
    public int Id { get; set; }
    public long CardNumber { get; set; }
    public int Cvc {  get; set; }
    public int Date {  get; set; }
}
