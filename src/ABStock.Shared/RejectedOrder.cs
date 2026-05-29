namespace ABStock.Shared;

public record RejectedOrder(
    Order? Order,
    string Reason
);
