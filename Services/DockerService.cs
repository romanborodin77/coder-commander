using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

namespace CoderCommander.Services;

/// <summary>
/// Сервис для управления Docker-контейнерами и образами через CLI.
/// Service for managing Docker containers and images via CLI.
/// </summary>
public sealed class DockerService
{
    private readonly IProcessService _proc;

    /// <summary>
    /// Создаёт экземпляр DockerService.
    /// Creates an instance of DockerService.
    /// </summary>
    /// <param name="proc">Сервис запуска процессов. / Process service.</param>
    public DockerService(IProcessService proc) => _proc = proc;

    private const string ContainerFormat = "{{.ID}}\t{{.Names}}\t{{.Image}}\t{{.Status}}\t{{.State}}";

    /// <summary>
    /// Возвращает список Docker-контейнеров (по умолчанию все, включая остановленные).
    /// Returns a list of Docker containers (all by default, including stopped).
    /// </summary>
    /// <param name="all">Включать остановленные контейнеры. / Include stopped containers.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Список контейнеров. / List of containers.</returns>
    public async Task<List<DockerContainer>> ContainersAsync(bool all = true, CancellationToken ct = default)
    {
        var args = all ? new[] { "ps", "-a", "--format", ContainerFormat }
                       : new[] { "ps", "--format", ContainerFormat };
        var r = await _proc.RunAsync("docker", args, ct: ct);
        var list = new List<DockerContainer>();
        if (!r.Success) return list;
        foreach (var line in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split('\t');
            if (p.Length == 5)
                list.Add(new DockerContainer(p[0], p[1], p[2], p[3], p[4]));
        }
        return list;
    }

    /// <summary>
    /// Возвращает список Docker-образов.
    /// Returns a list of Docker images.
    /// </summary>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Список образов. / List of images.</returns>
    public async Task<List<DockerImage>> ImagesAsync(CancellationToken ct = default)
    {
        var r = await _proc.RunAsync("docker", new[] { "images", "--format", "{{.ID}}\t{{.Repository}}\t{{.Tag}}\t{{.Size}}" }, ct: ct);
        var list = new List<DockerImage>();
        if (!r.Success) return list;
        foreach (var line in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split('\t');
            if (p.Length == 4) list.Add(new DockerImage(p[0], p[1], p[2], p[3]));
        }
        return list;
    }

    /// <summary>
    /// Запускает контейнер по ID или имени.
    /// Starts a container by ID or name.
    /// </summary>
    /// <param name="id">ID или имя контейнера. / Container ID or name.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения docker start. / Result of docker start.</returns>
    public async Task<ProcessResult> StartAsync(string id, CancellationToken ct = default)
        => await _proc.RunAsync("docker", new[] { "start", id }, ct: ct);

    /// <summary>
    /// Останавливает контейнер по ID или имени.
    /// Stops a container by ID or name.
    /// </summary>
    /// <param name="id">ID или имя контейнера. / Container ID or name.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения docker stop. / Result of docker stop.</returns>
    public async Task<ProcessResult> StopAsync(string id, CancellationToken ct = default)
        => await _proc.RunAsync("docker", new[] { "stop", id }, ct: ct);

    /// <summary>
    /// Принудительно удаляет контейнер (docker rm -f).
    /// Forcefully removes a container (docker rm -f).
    /// </summary>
    /// <param name="id">ID или имя контейнера. / Container ID or name.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения docker rm. / Result of docker rm.</returns>
    public async Task<ProcessResult> RemoveAsync(string id, CancellationToken ct = default)
        => await _proc.RunAsync("docker", new[] { "rm", "-f", id }, ct: ct);

    /// <summary>
    /// Принудительно удаляет образ (docker rmi -f).
    /// Forcefully removes an image (docker rmi -f).
    /// </summary>
    /// <param name="id">ID образа. / Image ID.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения docker rmi. / Result of docker rmi.</returns>
    public async Task<ProcessResult> RemoveImageAsync(string id, CancellationToken ct = default)
        => await _proc.RunAsync("docker", new[] { "rmi", "-f", id }, ct: ct);

    /// <summary>
    /// Возвращает последние 200 строк логов контейнера.
    /// Returns the last 200 log lines of a container.
    /// </summary>
    /// <param name="id">ID или имя контейнера. / Container ID or name.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Текст логов. / Log text.</returns>
    public async Task<string> LogsAsync(string id, CancellationToken ct = default)
        => (await _proc.RunAsync("docker", new[] { "logs", "--tail", "200", id }, ct: ct)).StdOut;

    /// <summary>
    /// Запускает композ-проект в фоне (docker compose up -d).
    /// Starts a compose project in detached mode (docker compose up -d).
    /// </summary>
    /// <param name="composeDir">Директория с docker-compose.yml. / Directory containing docker-compose.yml.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения docker compose up. / Result of docker compose up.</returns>
    public async Task<ProcessResult> ComposeUpAsync(string composeDir, CancellationToken ct = default)
        => await _proc.RunAsync("docker", new[] { "compose", "up", "-d" }, composeDir, ct);

    /// <summary>
    /// Останавливает композ-проект (docker compose down).
    /// Stops a compose project (docker compose down).
    /// </summary>
    /// <param name="composeDir">Директория с docker-compose.yml. / Directory containing docker-compose.yml.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения docker compose down. / Result of docker compose down.</returns>
    public async Task<ProcessResult> ComposeDownAsync(string composeDir, CancellationToken ct = default)
        => await _proc.RunAsync("docker", new[] { "compose", "down" }, composeDir, ct);

    /// <summary>
    /// Выполняет команду внутри контейнера через /bin/sh -c.
    /// Executes a command inside a container via /bin/sh -c.
    /// </summary>
    /// <param name="id">ID или имя контейнера. / Container ID or name.</param>
    /// <param name="command">Команда для выполнения. / Command to execute.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения docker exec. / Result of docker exec.</returns>
    public async Task<ProcessResult> ExecAsync(string id, string command, CancellationToken ct = default)
        => await _proc.RunAsync("docker", new[] { "exec", "-i", id, "/bin/sh", "-c", command }, ct: ct);
}

/// <summary>
/// Представляет Docker-контейнер (ID, имя, образ, статус, состояние).
/// Represents a Docker container (ID, name, image, status, state).
/// </summary>
/// <param name="Id">Идентификатор контейнера. / Container ID.</param>
/// <param name="Name">Имя контейнера. / Container name.</param>
/// <param name="Image">Имя образа. / Image name.</param>
/// <param name="Status">Человекочитаемый статус. / Human-readable status.</param>
/// <param name="State">Состояние (running, exited и т.д.). / State (running, exited, etc.).</param>
public sealed record DockerContainer(string Id, string Name, string Image, string Status, string State);
/// <summary>
/// Представляет Docker-образ (ID, репозиторий, тег, размер).
/// Represents a Docker image (ID, repository, tag, size).
/// </summary>
/// <param name="Id">Идентификатор образа. / Image ID.</param>
/// <param name="Repository">Репозиторий. / Repository.</param>
/// <param name="Tag">Тег образа. / Image tag.</param>
/// <param name="Size">Размер образа. / Image size.</param>
public sealed record DockerImage(string Id, string Repository, string Tag, string Size);
