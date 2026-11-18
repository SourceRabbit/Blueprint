// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.BackgroundTasks.Schedule;

namespace Blueprint.BackgroundTasks
{
    /// <summary>Defines a background task's work, description, status, and schedule.</summary>
    public abstract class BackgroundTask
    {
        private readonly string fName;
        private readonly string fDescription;
        private volatile BackgroundTaskSchedule fSchedule;
        private volatile EBackgroundTaskStatus fStatus = EBackgroundTaskStatus.Idle;

        /// <summary>Sets the task name, description, and initial schedule.</summary>
        /// <param name="name">The task name.</param>
        /// <param name="description">The task description.</param>
        /// <param name="schedule">The initial schedule.</param>
        /// <exception cref="ArgumentException">The name is empty.</exception>
        /// <exception cref="ArgumentNullException">No name, description, or schedule was provided.</exception>
        protected BackgroundTask(string name, string description, BackgroundTaskSchedule schedule)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(description);
            ArgumentNullException.ThrowIfNull(schedule);
            fName = name;
            fDescription = description;
            fSchedule = schedule;
        }

        /// <summary>The fixed task name.</summary>
        public string Name
        {
            get
            {
                return fName;
            }
        }

        /// <summary>The description of the task's purpose.</summary>
        public string Description
        {
            get
            {
                return fDescription;
            }
        }

        /// <summary>The execution status, updated internally while managing the task.</summary>
        public EBackgroundTaskStatus Status
        {
            get
            {
                return fStatus;
            }
            internal set
            {
                fStatus = value;
            }
        }

        /// <summary>The execution schedule, which can be replaced according to user settings.</summary>
        /// <exception cref="ArgumentNullException">No schedule was provided.</exception>
        public BackgroundTaskSchedule Schedule
        {
            get
            {
                return fSchedule;
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                fSchedule = value;
            }
        }

        /// <summary>Performs the task's work.</summary>
        /// <remarks>The implementation contains only the work and completes before returning, without async void. The execution manager updates Status externally and handles exceptions.</remarks>
        public abstract void Run();
    }
}
