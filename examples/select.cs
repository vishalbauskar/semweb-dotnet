using System;
using SemWeb;

public class Select {
	const string RDF = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
	const string FOAF = "http://xmlns.com/foaf/0.1/";
	
	static readonly Entity rdftype = RDF+"type";
	static readonly Entity foafPerson = FOAF+"Person";
	static readonly Entity foafknows = FOAF+"knows";
	static readonly Entity foafname = FOAF+"name";

	public static void Main() {
		Store store = new MemoryStore();
		store.Import(RdfReader.LoadFromUri(new Uri("http://dannyayers.com/misc/foaf/foaf.rdf")));
		
		Console.WriteLine("These are the people in the file:");
		foreach (Statement s in store.Select(new Statement(null, rdftype, foafPerson))) {
			foreach (Resource r in store.SelectObjects(s.Subject, foafname))
				Console.WriteLine(r);
		}
		Console.WriteLine();

		Console.WriteLine("And here's RDF/XML just for some of the file:");
		using (RdfWriter w = new RdfXmlWriter(Console.Out)) {
			store.Select(new Statement(null, foafname, null), w);
			store.Select(new Statement(null, foafknows, null), w);
		}
		Console.WriteLine();	
	}
}
