namespace MetaboyApi.Models
{
    public class CanClaimV2
    {
        public string Redeemable { get; set; } = "False";

        public string NftData { get; set; } = "";
        public int Amount { get; set; } = 0;
    }
}
