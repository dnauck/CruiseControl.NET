using System;
using System.Collections;
using System.Threading;
using System.Runtime.Remoting;
using System.Diagnostics;
using tw.ccnet.core.configuration;
using tw.ccnet.core.schedule;
using tw.ccnet.core.util;
using tw.ccnet.remote;

namespace tw.ccnet.core
{
	/// <summary>
	/// Main CruiseControl engine class.  Responsible for loading and launching CruiseControl projects.
	/// </summary>
	public class CruiseControl : ICruiseControl, ICruiseServer, IDisposable
	{
		private IConfigurationLoader _loader;
		private IDictionary _projects;
		private IList _projectIntegrators = new ArrayList();
		private bool _stopped = false;

		public CruiseControl(IConfigurationLoader loader)
		{
			_loader = loader;
			_loader.AddConfigurationChangedHandler(new ConfigurationChangedHandler(OnConfigurationChanged));
			_projects = _loader.LoadProjects();

			foreach (IProject project in _projects.Values)
			{
				AddProjectIntegrator(project);
			}
		}

		public IProject GetProject(string projectName)
		{
			return (IProject)_projects[projectName];
		}

		public IConfiguration Configuration
		{
			get { return _loader; }
		}

		public IntegrationResult RunIntegration(string projectName)
		{
			IProject project = GetProject(projectName);
			if (project == null) 
			{
				throw new CruiseControlException(String.Format("Cannot execute the specified project: {0}.  Project does not exist.", projectName));
			}
			return project.RunIntegration(BuildCondition.ForceBuild);
		}

		public void ForceBuild(string projectName)
		{
			IProject project = GetProject(projectName);

			// tell the project's schedule that we want a build forced
			project.Schedule.ForceBuild();
		}

		private void AddProjectIntegrator(IProject project)
		{
			if (project.Schedule!=null)
				_projectIntegrators.Add(new ProjectIntegrator(project.Schedule, project));
		}

		public ICollection Projects
		{
			get { return _projects.Values; }
		}

		public IList ProjectIntegrators
		{
			get { return _projectIntegrators; }
		}

		public CruiseControlStatus Status
		{
			get { return (_stopped) ? CruiseControlStatus.Stopped : CruiseControlStatus.Running; }
		}

		/// <summary>
		/// Starts each project's integration cycle.
		/// </summary>
		public void Start()
		{
			_stopped = false;

			foreach (IProjectIntegrator projectIntegrator in _projectIntegrators)
			{
				projectIntegrator.Start();
			}
		}

		/// <summary>
		/// Stops each project's integration cycle.
		/// </summary>
		public void Stop()
		{
			_stopped = true;

			foreach (IProjectIntegrator projectIntegrator in _projectIntegrators)
			{
				projectIntegrator.Stop();
			}
		}

		public void Abort()
		{
			foreach (IProjectIntegrator projectIntegrator in _projectIntegrators)
			{
				projectIntegrator.Abort();
			}		
		}

		protected void OnConfigurationChanged()
		{
			ReloadConfiguration();
		}

		private void ReloadConfiguration()
		{
			_projects = _loader.LoadProjects();

			IDictionary schedulerMap = CreateSchedulerMap();
			foreach (IProjectIntegrator scheduler in schedulerMap.Values)
			{
				IProject project = (IProject)_projects[scheduler.Project.Name];
				if (project == null)
				{
					// project has been removed, so stop scheduler and remove
					scheduler.Stop();
					_projectIntegrators.Remove(scheduler);
				}
				else
				{
					scheduler.Project = project;
					scheduler.Schedule = project.Schedule;
				}
			}

			foreach (IProject project in _projects.Values)
			{
				IProjectIntegrator scheduler = (IProjectIntegrator)schedulerMap[project.Name];
				if (scheduler == null)
				{
					// create new scheduler
					IProjectIntegrator newScheduler = new ProjectIntegrator(project.Schedule, project);
					_projectIntegrators.Add(newScheduler);
					newScheduler.Start();
				}
			}
		}

		private IDictionary CreateSchedulerMap()
		{
			Hashtable schedulerList = new Hashtable();
			foreach (ProjectIntegrator scheduler in _projectIntegrators)
			{
				schedulerList.Add(scheduler.Project.Name, scheduler);
			}
			return schedulerList;
		}

		/// <summary>
		/// Stops all integration threads when garbage collected.
		/// </summary>
		public void Dispose()
		{
			Stop();
		}

		public void WaitForExit()
		{
			foreach (IProjectIntegrator scheduler in _projectIntegrators)
			{
				scheduler.WaitForExit();
			}
		}

		/// <summary>
		/// Gets the most recent build status across all projects for this CruiseControl.NET
		/// instance.
		/// </summary>
		/// <returns></returns>
		public IntegrationStatus GetLatestBuildStatus()
		{
			// TODO determine the most recent where multiple projects exist, rather than simply returning the first
			foreach (IProject project in Projects) 
			{
				return project.GetLatestBuildStatus();
			}

			return IntegrationStatus.Unknown;
		}

		public ProjectActivity CurrentProjectActivity()
		{
			foreach (IProject project in Projects) 
			{
				return project.CurrentActivity;
			}

			return ProjectActivity.Unknown;
		}

	}
}
