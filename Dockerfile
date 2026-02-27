# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY ["ReRhythm.Web/ReRhythm.Web.csproj", "ReRhythm.Web/"]
COPY ["ReRhythm.Core/ReRhythm.Core.csproj", "ReRhythm.Core/"]
COPY ["ReRhythm.Infrastructure/ReRhythm.Infrastructure.csproj", "ReRhythm.Infrastructure/"]

# Restore dependencies
RUN dotnet restore "ReRhythm.Web/ReRhythm.Web.csproj"

# Copy all source code
COPY . .

# Build and publish
WORKDIR "/src/ReRhythm.Web"
RUN dotnet build "ReRhythm.Web.csproj" -c Release -o /app/build
RUN dotnet publish "ReRhythm.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install security updates
RUN apt-get update && apt-get upgrade -y && rm -rf /var/lib/apt/lists/*

# Create non-root user for security
RUN groupadd -r appuser && useradd -r -g appuser appuser

# Copy published app
COPY --from=build /app/publish .

# Set ownership
RUN chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Expose port 8080 (App Runner default)
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/ || exit 1

# Start application
ENTRYPOINT ["dotnet", "ReRhythm.Web.dll"]
