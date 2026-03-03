using System;
using System.Collections.Concurrent;

namespace Antigravity02.Tools
{
    public enum TaskStatus
    {
        Pending,
        Running,
        Completed,
        Failed
    }

    public class TaskItem
    {
        public string TaskId { get; set; } = Guid.NewGuid().ToString("N");
        public string Assignee { get; set; } = string.Empty;
        public string Request { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public TaskStatus Status { get; set; } = TaskStatus.Pending;
        public DateTime CreatedTime { get; set; } = DateTime.Now;
    }

    public static class TaskOrchestrator
    {
        private static readonly ConcurrentDictionary<string, TaskItem> _tasks = new ConcurrentDictionary<string, TaskItem>();

        public static TaskItem AddTask(string expert, string question)
        {
            var task = new TaskItem
            {
                TaskId = Guid.NewGuid().ToString("N"),
                Assignee = expert,
                Request = question,
                Result = string.Empty,
                Status = TaskStatus.Pending,
                CreatedTime = DateTime.Now
            };

            _tasks.TryAdd(task.TaskId, task);
            return task;
        }

        public static void UpdateTask(string id, TaskStatus status, string result)
        {
            if (_tasks.TryGetValue(id, out var task))
            {
                task.Status = status;
                if (result != null)
                {
                    task.Result = result;
                }
            }
        }

        public static TaskItem GetTask(string id)
        {
            _tasks.TryGetValue(id, out var task);
            return task;
        }
    }
}
