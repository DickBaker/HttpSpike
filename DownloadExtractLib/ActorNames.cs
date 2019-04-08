namespace DownloadExtractLib
{
    /// <summary>
    /// Helper class that provides basic name and address information for Actors.
    /// That way if we need to change the name of an actor, we only need to do it in one place.
    /// </summary>
    public static class ActorNames
    {
        public const string STAGENAME = "Equity",
            MONIKER = "akka://" + STAGENAME + "/ user",
            DOWNLOADWORKERROOT = "DownloadActor_",
            PARSEWORKERROOT = "ParseActor_";

        /// <summary>
        ///     Responsible for parsing HTML files (using child routees)
        /// </summary>
        /// <remarks>
        ///     input files probably just downloaded by DownloadCoordinatorActor
        /// </remarks>
        public static readonly ActorData ParseCoordinatorActor = new ActorData("ParseCoordinatorActor", MONIKER); // /user/ParseCoordinator

        /// <summary>
        /// Responsible for downloading pages from internet-based webservers (using child routees)
        /// </summary>
        /// <remarks>
        ///     input URLs possibly just extracted by ParseCoordinatorActor
        /// </remarks>
		public static readonly ActorData DownloadCoordinatorActor = new ActorData("DownloadCoordinatorActor", MONIKER); // /user/DownloadCoordinator

        /// <summary>
        ///     Responsible for processing A.mhtml files (extracts MIME content to individual files and relativises into A.html)
        /// </summary>
        /// <remarks>
        ///     format originally from IE ("save as web complete") having embedded MIME [https://tools.ietf.org/html/rfc2045] content as downloaded for original render
        /// </remarks>
		public static readonly ActorData MhtmlCoordinatorActor = new ActorData("MhtmlCoordinator", MONIKER); // /user/MhtmlCoordinator
    }

    /// <summary>
    /// Meta-data class for working with high-level Actor names and paths
    /// </summary>
    public class ActorData
    {
        public ActorData(string name, string parent)
        {
            Path = parent + "/" + name;
            Name = name;
        }

        public string Name { get; }

        public string Path { get; }
    }
}
