namespace Domain;

public sealed record CachedPortfolioData(PortfolioData Portfolio, DateTimeOffset CapturedAt);
