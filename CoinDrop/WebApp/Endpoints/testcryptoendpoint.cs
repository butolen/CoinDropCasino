using CoinDrop.config;
using Microsoft.Extensions.Options;
using Solnet.Wallet;
using Solnet.Wallet.Bip39;

namespace WebApp.Endpoints;

public static class TestCryptoEndpoints
{
    public static IEndpointRouteBuilder MapTestEndpoints(this IEndpointRouteBuilder app)
    {

        app.MapGet("/dev/treasury-check", (IOptions<CryptoConfig> cfg) =>
        {
            var mnemonic = new Mnemonic(cfg.Value.TreasuryMnemonic, WordList.English);
            var wallet = new Wallet(mnemonic);
            var account = wallet.GetAccount(0);

            return Results.Ok(new
            {
                DerivedAddress = account.PublicKey,
                ConfiguredAddress = cfg.Value.TreasuryAddress
            });
        });
        return app;
    }
}