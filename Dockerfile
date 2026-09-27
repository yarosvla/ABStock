FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/ src/
RUN dotnet restore src/ABStock.UI/ABStock.UI.csproj

RUN dotnet publish src/ABStock.UI/ABStock.UI.csproj \
    --configuration Release --no-restore --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

RUN mkdir -p /app/data /home/app/.aspnet/DataProtection-Keys \
    && chown -R app:app /app/data /home/app/.aspnet
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ABStock.UI.dll"]
