using CoinDrop.config;
using CoinDrop.services.interfaces;

namespace CoinDrop.services;

using Solnet.Wallet;
using Solnet.Wallet.Bip39;
using Microsoft.Extensions.Options;

public class SolanaWalletService :ISolanService
{
    private readonly Wallet _masterWallet;

    public SolanaWalletService(IOptions<CryptoConfig> cfg)
    {
        _masterWallet = new Wallet(
            new Mnemonic(cfg.Value.MasterMnemonic, WordList.English)
        );
    }

    public string GetUserDepositAddress(int userIndex)
    {
        // deterministisch, stabil
        var account = _masterWallet.GetAccount(userIndex);
        return account.PublicKey;
    }
}