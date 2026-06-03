# TaskApi

Тестовое задание на `C# / .NET 9` с использованием:

- `ASP.NET Core Minimal API`
- `EF Core`
- `PostgreSQL`
- `RabbitMQ`

Реализовано:

- создание задачи `POST /tasks`
- получение всех задач `GET /tasks`
- получение задач с пагинацией `GET /tasks?offset=0&limit=20`
- завершение задачи `PUT /tasks/{id}/complete`
- удаление задачи `DELETE /tasks/{id}`
- валидация `Title`
- публикация события `task.completed` в `task.events`
- мягкая обработка недоступного `RabbitMQ`
- набор интеграционных тестов на ключевые сценарии

## Структура

- [TaskApi/Program.cs](TaskApi/Program.cs)
- [TaskApi/Endpoints/TaskEndpoints.cs](TaskApi/Endpoints/TaskEndpoints.cs)
- [TaskApi/Data/TaskDbContext.cs](TaskApi/Data/TaskDbContext.cs)
- [database/init/001-init-task-api.sql](database/init/001-init-task-api.sql)
- [TaskApi/Models/TaskItem.cs](TaskApi/Models/TaskItem.cs)
- [TaskApi/Contracts/Requests/CreateTaskRequest.cs](TaskApi/Contracts/Requests/CreateTaskRequest.cs)
- [TaskApi/Contracts/Responses/TaskItemResponse.cs](TaskApi/Contracts/Responses/TaskItemResponse.cs)
- [TaskApi/Messaging/RabbitMqTaskEventPublisher.cs](TaskApi/Messaging/RabbitMqTaskEventPublisher.cs)
- [TaskApi.Tests/CompleteTaskIntegrationTests.cs](TaskApi.Tests/CompleteTaskIntegrationTests.cs)

## Как запустить

По умолчанию приложение использует такие настройки:

- PostgreSQL: `Host=localhost;Port=5432;Database=task_api;Username=postgres;Password=postgres`
- RabbitMQ: `localhost:5672`, `guest/guest`
- API: `http://localhost:5115`

Строка подключения к БД хранится в конфигурации приложения:

- [TaskApi/appsettings.json](TaskApi/appsettings.json)

Это локальные значения по умолчанию для запуска через Docker.

При необходимости настройки можно переопределить:

- напрямую в `appsettings.json`
- через переменные окружения, например:

```powershell
$env:ConnectionStrings__TasksDb='Host=localhost;Port=5432;Database=task_api;Username=postgres;Password=postgres'
$env:RabbitMq__HostName='localhost'
```

Сначала поднять инфраструктуру:

```powershell
docker compose up -d postgres rabbitmq
```

SQL-инициализация PostgreSQL лежит в [database/init/001-init-task-api.sql](database/init/001-init-task-api.sql).
Этот скрипт выполняется контейнером автоматически только при первом создании пустого volume.
Если volume уже существует, для повторной инициализации нужно пересоздать его:

```powershell
docker compose down -v
docker compose up -d postgres rabbitmq
```

Запуск приложения:

```powershell
dotnet run --project .\TaskApi\TaskApi.csproj
```

## Как проверить API

Создать задачу:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri 'http://localhost:5115/tasks' `
  -ContentType 'application/json' `
  -Body '{"title":"Buy milk","priority":2}'
```

Получить все задачи:

```powershell
Invoke-RestMethod -Method Get -Uri 'http://localhost:5115/tasks'
```

`GET /tasks` всегда возвращает объект с полями `items`, `nextOffset` и `totalCount`. Без параметров в `items` приходит полный список задач. Если следующей порции данных нет, `nextOffset` равен `0`. Если передать `offset` и `limit`, endpoint вернет одну порцию результатов.

Получить задачи с пагинацией:

```powershell
Invoke-RestMethod -Method Get -Uri 'http://localhost:5115/tasks?offset=0&limit=20'
```

Завершить задачу:

```powershell
Invoke-RestMethod -Method Put -Uri 'http://localhost:5115/tasks/<TASK_ID>/complete'
```

Удалить задачу:

```powershell
Invoke-RestMethod -Method Delete -Uri 'http://localhost:5115/tasks/<TASK_ID>'
```

## Как запустить тесты

```powershell
dotnet test .\TaskApi.Tests\TaskApi.Tests.csproj
```

## Что проверено вручную

- приложение успешно стартует с реальными `PostgreSQL` и `RabbitMQ` из `docker compose`
- создание задачи сохраняет запись в PostgreSQL
- завершение задачи публикует событие в `RabbitMQ` exchange `task.events` с routing key `task.completed`

## Что покрывают интеграционные тесты

- API запускается против реального `PostgreSQL`
- создание задачи через `POST /tasks`
- получение всех задач через `GET /tasks`
- завершение задачи через `PUT /tasks/{id}/complete`
- пагинация в `GET /tasks?offset=...&limit=...`
- конкурентное завершение задачи: один запрос успешен, второй получает `409 Conflict`
- успешное завершение задачи даже при ошибке публикации события
- публикация события через подмененный `ITaskEventPublisher`

## Принятые упрощения

- Вместо миграций используется `EnsureCreated`, чтобы не раздувать решение.
- Для удобного локального старта также добавлен SQL-скрипт начальной инициализации PostgreSQL.
- Интеграционные тесты используют реальный `PostgreSQL`, но публикация в них проверяется через подмененный `ITaskEventPublisher`, без реального RabbitMQ.
- Реальный сценарий публикации в RabbitMQ дополнительно проверен вручную через поднятый broker.
- При недоступном RabbitMQ задача все равно завершается, а ошибка уходит в лог, как и требовалось по условию.
