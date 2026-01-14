using System.Text.Json.Serialization;

namespace CoinDrop;

public enum CardSuit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades
}

public enum CardValue
{
    Ace = 1,
    Two = 2,
    Three = 3,
    Four = 4,
    Five = 5,
    Six = 6,
    Seven = 7,
    Eight = 8,
    Nine = 9,
    Ten = 10,
    Jack = 11,
    Queen = 12,
    King = 13
}

public enum BlackjackAction
{
    Hit,
    Stand,
    DoubleDown,
    Split,
    Surrender
}

public enum GameStatus
{
    Active,
    PlayerWon,
    DealerWon,
    Draw,
    PlayerBusted,
    DealerBusted,
    Blackjack,
    Surrendered
}

public class Card
{
    public CardSuit Suit { get; set; }
    public CardValue Value { get; set; }
    
    // Dictionary für Blackjack-Werte
    private static readonly Dictionary<CardValue, int> BlackjackValues = new()
    {
        { CardValue.Ace, 11 },
        { CardValue.Two, 2 },
        { CardValue.Three, 3 },
        { CardValue.Four, 4 },
        { CardValue.Five, 5 },
        { CardValue.Six, 6 },
        { CardValue.Seven, 7 },
        { CardValue.Eight, 8 },
        { CardValue.Nine, 9 },
        { CardValue.Ten, 10 },
        { CardValue.Jack, 10 },
        { CardValue.Queen, 10 },
        { CardValue.King, 10 }
    };
    
    // Dictionary für Bildnamen
    private static readonly Dictionary<CardValue, string> ValueImageNames = new()
    {
        { CardValue.Ace, "ace" },
        { CardValue.Two, "2" },
        { CardValue.Three, "3" },
        { CardValue.Four, "4" },
        { CardValue.Five, "5" },
        { CardValue.Six, "6" },
        { CardValue.Seven, "7" },
        { CardValue.Eight, "8" },
        { CardValue.Nine, "9" },
        { CardValue.Ten, "10" },
        { CardValue.Jack, "jack" },
        { CardValue.Queen, "queen" },
        { CardValue.King, "king" }
    };
    
    private static readonly Dictionary<CardSuit, string> SuitImageNames = new()
    {
        { CardSuit.Clubs, "clubs" },
        { CardSuit.Diamonds, "diamonds" },
        { CardSuit.Hearts, "hearts" },
        { CardSuit.Spades, "spades" }
    };
    
    [JsonIgnore]
    public string ImagePath => $"/images/BJCards/{ImageName}.png";
    
    [JsonIgnore]
    public string ImageName => $"{ValueImageNames[Value]}_of_{SuitImageNames[Suit]}";
    
    [JsonIgnore]
    public string ValueName => ValueImageNames[Value];
    
    [JsonIgnore]
    public string SuitName => SuitImageNames[Suit];
    
    public int GetBlackjackValue(bool aceAsOne = false)
    {
        if (Value == CardValue.Ace && aceAsOne)
            return 1;
        return BlackjackValues[Value];
    }
    
    public bool IsFaceCard() => Value == CardValue.Jack || Value == CardValue.Queen || Value == CardValue.King;
    public bool IsAce() => Value == CardValue.Ace;
    
    public bool HasSameValueAs(Card other)
    {
        if (this.IsAce() && other.IsAce()) return true;
        if (this.IsFaceCard() && other.IsFaceCard()) return true;
        return this.Value == other.Value;
    }
}

public class BlackjackGame
{
    public string GameId { get; set; } = Guid.NewGuid().ToString();
    public int UserId { get; set; }
    public double BetAmount { get; set; }
    public List<Card> PlayerHand { get; set; } = new();
    public List<Card> DealerHand { get; set; } = new();
    public List<Card> SplitHand { get; set; } = new();
    public bool IsSplit { get; set; }
    public bool IsSplitActive { get; set; }
    public GameStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public double WinAmount { get; set; }
    
    // GameSession ID für Datenbank-Referenz
    public int GameSessionId { get; set; }
    
    // Das echte Deck für dieses Spiel
    public List<Card> Deck { get; set; } = new();
    
    // Random-Generator für dieses Spiel
    private Random random = new Random();
    
    [JsonIgnore]
    public List<Card> ActivePlayerHand => IsSplit && IsSplitActive ? SplitHand : PlayerHand;
    
    public int GetHandValue(List<Card> hand)
    {
        var value = hand.Sum(card => card.GetBlackjackValue());
        var aceCount = hand.Count(c => c.IsAce());
        
        while (value > 21 && aceCount > 0)
        {
            value -= 10;
            aceCount--;
        }
        return value;
    }
    
    public bool HasBlackjack(List<Card> hand) => hand.Count == 2 && GetHandValue(hand) == 21;
    public bool IsBusted(List<Card> hand) => GetHandValue(hand) > 21;
    
    public bool CanSplit() => !IsSplit && PlayerHand.Count == 2 && PlayerHand[0].HasSameValueAs(PlayerHand[1]);
    public bool CanDoubleDown() => Status == GameStatus.Active && !IsSplit && PlayerHand.Count == 2;
    
    // ECHTE KARTENZIEHUNG: Zieht eine zufällige Karte aus dem Deck
    public Card DrawCard()
    {
        if (Deck.Count == 0)
        {
            // Neue 6 Decks erstellen und mischen
            InitializeDeck();
        }
        
        // Zufällige Karte ziehen
        var index = random.Next(Deck.Count);
        var card = Deck[index];
        Deck.RemoveAt(index);
        
        return card;
    }
    
    // Initialisiert das Spiel mit einem frischen Deck
    public void InitializeGame()
    {
        // Neues Deck erstellen
        InitializeDeck();
        
        // Karten an Spieler und Dealer verteilen
        PlayerHand.Add(DrawCard());
        DealerHand.Add(DrawCard());
        PlayerHand.Add(DrawCard());
        DealerHand.Add(DrawCard());
        
        // Blackjack prüfen
        if (HasBlackjack(PlayerHand))
        {
            Status = GameStatus.Blackjack;
            WinAmount = BetAmount * 2.5; // 3:2 Auszahlung
        }
        else
        {
            Status = GameStatus.Active;
        }
    }
    
    // Erstellt und mischt ein neues Deck (6 Decks wie im Casino)
    private void InitializeDeck()
    {
        Deck.Clear();
        
        // 6 Decks erstellen
        for (int i = 0; i < 6; i++)
        {
            foreach (CardSuit suit in Enum.GetValues(typeof(CardSuit)))
            {
                foreach (CardValue value in Enum.GetValues(typeof(CardValue)))
                {
                    Deck.Add(new Card { Suit = suit, Value = value });
                }
            }
        }
        
        // Deck mischen (Fisher-Yates Shuffle)
        for (int i = Deck.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (Deck[j], Deck[i]) = (Deck[i], Deck[j]);
        }
    }
    
    // Dealer zieht Karten nach den Regeln
    public void DealerPlay()
    {
        // Dealer muss ziehen bis 17 oder mehr
        while (GetHandValue(DealerHand) < 17)
        {
            DealerHand.Add(DrawCard());
        }
    }
    
    // Bestimmt den Gewinner
    public void DetermineWinner()
    {
        var playerValue = GetHandValue(ActivePlayerHand);
        var dealerValue = GetHandValue(DealerHand);
        
        if (IsBusted(ActivePlayerHand))
        {
            Status = GameStatus.PlayerBusted;
            WinAmount = 0;
        }
        else if (IsBusted(DealerHand))
        {
            Status = GameStatus.DealerBusted;
            WinAmount = BetAmount * 2; // 1:1 Auszahlung
        }
        else if (playerValue > dealerValue)
        {
            Status = GameStatus.PlayerWon;
            WinAmount = BetAmount * 2; // 1:1 Auszahlung
        }
        else if (dealerValue > playerValue)
        {
            Status = GameStatus.DealerWon;
            WinAmount = 0;
        }
        else
        {
            Status = GameStatus.Draw;
            WinAmount = BetAmount; // Einsatz zurück
        }
    }
    
    // Serialisierungs-Helper für Frontend
    public object ToFrontendObject()
    {
        return new
        {
            GameId,
            BetAmount,
            Status = Status.ToString(),
            GameSessionId,
            PlayerHand = PlayerHand.Select(c => new
            {
                c.ValueName,
                c.SuitName,
                c.ImagePath,
                c.ImageName,
                Value = c.GetBlackjackValue(),
                IsAce = c.IsAce(),
                IsFaceCard = c.IsFaceCard()
            }),
            DealerHand = Status != GameStatus.Active 
                ? DealerHand.Select(c => new
                {
                    c.ValueName,
                    c.SuitName,
                    c.ImagePath,
                    c.ImageName,
                    Value = c.GetBlackjackValue(),
                    IsAce = c.IsAce(),
                    IsFaceCard = c.IsFaceCard()
                })
                : new[] 
                {
                    new 
                    { 
                        ValueName = DealerHand[0].ValueName,
                        SuitName = DealerHand[0].SuitName,
                        ImagePath = DealerHand[0].ImagePath,
                        ImageName = DealerHand[0].ImageName,
                        Value = DealerHand[0].GetBlackjackValue(),
                        IsAce = DealerHand[0].IsAce(),
                        IsFaceCard = DealerHand[0].IsFaceCard()
                    },
                    new 
                    { 
                        ValueName = "hidden",
                        SuitName = "hidden",
                        ImagePath = "/images/BJCards/card_back.png",
                        ImageName = "card_back",
                        Value = 0,
                        IsAce = false,
                        IsFaceCard = false
                    }
                },
            SplitHand = SplitHand.Select(c => new
            {
                c.ValueName,
                c.SuitName,
                c.ImagePath,
                c.ImageName,
                Value = c.GetBlackjackValue(),
                IsAce = c.IsAce(),
                IsFaceCard = c.IsFaceCard()
            }),
            IsSplit,
            IsSplitActive,
            PlayerValue = GetHandValue(PlayerHand),
            DealerValue = Status != GameStatus.Active ? GetHandValue(DealerHand) : DealerHand[0].GetBlackjackValue(),
            SplitValue = GetHandValue(SplitHand),
            CanSplit = CanSplit(),
            CanDoubleDown = CanDoubleDown(),
            WinAmount,
            CreatedAt,
            EndedAt,
            CardsInDeck = Deck.Count // Debug info
        };
    }
    
    public class GameResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public object? Data { get; set; }
        
        public GameResponse(bool success, string message, object? data = null)
        {
            Success = success;
            Message = message;
            Data = data;
        }
    }
}