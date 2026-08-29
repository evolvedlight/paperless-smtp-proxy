# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["HomeSmtpServer.csproj", "./"]
RUN dotnet restore "HomeSmtpServer.csproj"

# Copy remaining source code and publish
COPY . .
RUN dotnet publish "HomeSmtpServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Environment configuration
ENV ASPNETCORE_HTTP_PORTS=8080
ENV SmtpServer__Port=25
ENV SmtpServer__ServerName=paperless.brown.bg
ENV SmtpServer__AllowAnyRecipient=true
ENV Paperless__Enabled=false
ENV Paperless__BaseUrl=http://paperless-web:8000
ENV Paperless__ApiToken=

# Expose Web UI (8080) and SMTP (25)
EXPOSE 8080
EXPOSE 25

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HomeSmtpServer.dll"]
