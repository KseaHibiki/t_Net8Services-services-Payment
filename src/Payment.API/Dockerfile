FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY shared/Shop.Events/Shop.Events.csproj shared/Shop.Events/
COPY services/Payment/src/Payment.Domain/Payment.Domain.csproj services/Payment/src/Payment.Domain/
COPY services/Payment/src/Payment.Application/Payment.Application.csproj services/Payment/src/Payment.Application/
COPY services/Payment/src/Payment.Infrastructure/Payment.Infrastructure.csproj services/Payment/src/Payment.Infrastructure/
COPY services/Payment/src/Payment.API/Payment.API.csproj services/Payment/src/Payment.API/

RUN dotnet restore services/Payment/src/Payment.API/Payment.API.csproj

COPY shared/Shop.Events/ shared/Shop.Events/
COPY services/Payment/src/Payment.Domain/ services/Payment/src/Payment.Domain/
COPY services/Payment/src/Payment.Application/ services/Payment/src/Payment.Application/
COPY services/Payment/src/Payment.Infrastructure/ services/Payment/src/Payment.Infrastructure/
COPY services/Payment/src/Payment.API/ services/Payment/src/Payment.API/

RUN dotnet publish services/Payment/src/Payment.API/Payment.API.csproj -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Payment.API.dll"]
