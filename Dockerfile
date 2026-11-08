# Stage 1: Build, Wolverine codegen & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["Domain/Studium-Booking.Domain.csproj", "Domain/"]
COPY ["Data/Studium-Booking.Data.csproj", "Data/"]
COPY ["Services/Studium-Booking.Services.csproj", "Services/"]
COPY ["Api/Studium-Booking.Api.csproj", "Api/"]
RUN dotnet restore "Api/Studium-Booking.Api.csproj"

COPY . .

# Pre-generate Wolverine handler code so production loads it statically (TypeLoadMode.Static)
RUN dotnet build "Api/Studium-Booking.Api.csproj" -c Release --no-restore
RUN ASPNETCORE_ENVIRONMENT=Production dotnet run --project "Api/Studium-Booking.Api.csproj" -c Release --no-build --no-launch-profile -- codegen write

RUN dotnet publish "Api/Studium-Booking.Api.csproj" -c Release --no-restore -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Studium-Booking.Api.dll"]
