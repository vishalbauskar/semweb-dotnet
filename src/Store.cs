using System;
using System.Collections;
using System.Data;

namespace SemWeb {
	
	public interface StatementSource {
		void Select(StatementSink sink);
	}
	
	public interface QueryableSource {
		bool Contains(Statement template);
		void Select(Statement template, StatementSink sink);
		void Select(Statement[] templates, StatementSink sink);
	}

	public interface StatementSink {
		bool Add(Statement statement);
	}

	internal class StatementCounterSink : StatementSink {
		int counter = 0;
		
		public int StatementCount { get { return counter; } }
		
		public bool Add(Statement statement) {
			counter++;
			return true;
		}
	}

	internal class StatementExistsSink : StatementSink {
		bool exists = false;
		
		public bool Exists { get { return exists; } }
		
		public bool Add(Statement statement) {
			exists = true;
			return false;
		}
	}

	public abstract class Store : StatementSource, QueryableSource, StatementSink {
		
		Entity rdfType;
		
		public static StatementSource CreateForInput(string spec) {
			return (StatementSource)Create(spec, false);
		}		
		
		public static StatementSink CreateForOutput(string spec) {
			return (StatementSink)Create(spec, true);
		}
		
		private static object Create(string spec, bool output) {
			string type = spec;
			
			int c = spec.IndexOf(':');
			if (c != -1) {
				type = spec.Substring(0, c);
				spec = spec.Substring(c+1);
			} else {
				spec = "";
			}
			
			switch (type) {
				case "mem":
					return new MemoryStore();
				case "xml":
					if (spec == "") throw new ArgumentException("Use: xml:filename");
					if (output) {
						return new RdfXmlWriter(spec);
					} else {
						return new RdfXmlReader(spec);
					}
				case "n3":
				case "ntriples":
				case "nt":
				case "turtle":
					if (spec == "") throw new ArgumentException("Use: format:filename");
					if (output) {
						N3Writer ret = new N3Writer(spec);
						switch (type) {
							case "nt": case "ntriples":
								ret.Format = N3Writer.Formats.NTriples;
								break;
							case "turtle":
								ret.Format = N3Writer.Formats.Turtle;
								break;
						}
						return ret;
					} else {
						return new N3Reader(spec);
					}
				/*case "file":
					if (spec == "") throw new ArgumentException("Use: format:filename");
					if (output) throw new ArgumentException("The FileStore does not support writing.");
					return new SemWeb.Stores.FileStore(spec);*/
				case "sqlite":
				case "mysql":
					if (spec == "") throw new ArgumentException("Use: sqlite|mysql:table:connection-string");
				
					c = spec.IndexOf(':');
					if (c == -1) throw new ArgumentException("Invalid format for sqlite/mysql spec parameter (table:constring).");
					string table = spec.Substring(0, c);
					spec = spec.Substring(c+1);
					
					string classtype = null;
					if (type == "sqlite") {
						classtype = "SemWeb.Stores.SqliteStore, SemWeb.SqliteStore";
						spec = spec.Replace(";", ",");
					} else if (type == "mysql") {
						classtype = "SemWeb.Stores.MySQLStore, SemWeb.MySQLStore";
					}
					Type ttype = Type.GetType(classtype);
					if (ttype == null)
						throw new NotSupportedException("The storage type in <" + classtype + "> could not be found.");
					return Activator.CreateInstance(ttype, new object[] { spec, table });
				default:
					throw new ArgumentException("Unknown parser type: " + type);
			}
		}
		
		protected Store() {
			rdfType = new Entity("http://www.w3.org/1999/02/22-rdf-syntax-ns#type");
		}
		
		public abstract int StatementCount { get; }

		public abstract void Clear();

		public Entity[] GetEntitiesOfType(Entity type) {
			ArrayList entities = new ArrayList();
			
			IEnumerable result = Select(new Statement(null, rdfType, type));
			foreach (Statement s in result) {
				entities.Add(s.Subject);
			}
			
			return (Entity[])entities.ToArray(typeof(Entity));
		}
		
		bool StatementSink.Add(Statement statement) {
			Add(statement);
			return true;
		}
		
		public abstract void Add(Statement statement);
		
		public abstract void Remove(Statement statement);

		public virtual void Import(StatementSource source) {
			source.Select(this);
		}
		
		public abstract Entity[] GetAllEntities();
		
		public abstract Entity[] GetAllPredicates();
		
		public abstract Entity[] GetAllMetas();
		
		public virtual bool Contains(Statement template) {
			return Contains(this, template);
		}

		internal static bool Contains(QueryableSource source, Statement template) {
			StatementExistsSink sink = new StatementExistsSink();
			source.Select(template, sink);
			return sink.Exists;
		}
		
		public void Select(StatementSink result) {
			Select(Statement.All, result);
		}
		
		public abstract void Select(Statement template, StatementSink result);
		
		public abstract void Select(Statement[] templates, StatementSink result);
		
		public SelectResult Select(Statement template) {
			return new SelectResult.Single(this, template);
		}
		
		public SelectResult Select(Statement[] templates) {
			return new SelectResult.Multi(this, templates);
		}
		
		public Resource[] SelectObjects(Entity subject, Entity predicate) {
			Hashtable resources = new Hashtable();
			foreach (Statement s in Select(new Statement(subject, predicate, null)))
				if (!resources.ContainsKey(s.Object))
					resources[s.Object] = s.Object;
			return (Resource[])new ArrayList(resources.Keys).ToArray(typeof(Resource));
		}
		public Entity[] SelectSubjects(Entity predicate, Resource @object) {
			Hashtable resources = new Hashtable();
			foreach (Statement s in Select(new Statement(null, predicate, @object)))
				if (!resources.ContainsKey(s.Subject))
					resources[s.Subject] = s.Subject;
			return (Entity[])new ArrayList(resources.Keys).ToArray(typeof(Entity));
		}
		
		public abstract void Replace(Entity find, Entity replacement);
		
		public abstract void Replace(Statement find, Statement replacement);
		
		public virtual Entity[] FindEntities(Statement[] filters) {
			return FindEntities(this, filters);
		}
		
		internal static Entity[] FindEntities(QueryableSource source, Statement[] filters) {
			Hashtable ents = new Hashtable();
			source.Select(filters[0], new FindEntitiesSink(ents, spom(filters[0])));
			for (int i = 1; i < filters.Length; i++) {
				Hashtable ents2 = new Hashtable();
				source.Select(filters[i], new FindEntitiesSink(ents2, spom(filters[i])));

				Hashtable ents3 = new Hashtable();
				if (ents.Count < ents2.Count) {
					foreach (Entity r in ents.Keys)
						if (ents2.ContainsKey(r))
							ents3[r] = r;
				} else {
					foreach (Entity r in ents2.Keys)
						if (ents.ContainsKey(r))
							ents3[r] = r;
				}
				ents = ents3;
			}
			
			ArrayList ret = new ArrayList();
			ret.AddRange(ents.Keys);
			return (Entity[])ret.ToArray(typeof(Entity));
		}
		
		private static int spom(Statement s) {
			if (s.Subject == null) return 0;
			if (s.Predicate == null) return 1;
			if (s.Object == null) return 2;
			if (s.Meta == null) return 3;
			throw new InvalidOperationException("A statement did not have a null field.");
		}
		
		private class FindEntitiesSink : StatementSink {
			Hashtable ents;
			int spom;
			public FindEntitiesSink(Hashtable ents, int spom) { this.ents = ents; this.spom = spom; }
			public bool Add(Statement s) {
				Entity e = null;
				if (spom == 0) e = s.Subject;
				if (spom == 1) e = s.Predicate;
				if (spom == 2) e = s.Object as Entity;
				if (spom == 3) e = s.Meta;
				if (e != null) ents[e] = ents;
				return true;
			}
		}
		
		public void Write(RdfWriter writer) {
			writer.Write(this);
		}
		
		public void Write(System.IO.TextWriter writer) {
			using (RdfWriter w = new N3Writer(writer)) {
				Write(w);
			}
		}
		
		protected object GetResourceKey(Resource resource) {
			return resource.GetResourceKey(this);
		}

		protected void SetResourceKey(Resource resource, object value) {
			resource.SetResourceKey(this, value);
		}
		
	}

	public abstract class SelectResult : StatementSource, IEnumerable {
		MemoryStore ms;
		internal SelectResult() { }
		public abstract void Select(StatementSink sink);
		public IEnumerator GetEnumerator() {
			return Buffer().Statements.GetEnumerator();
		}
		public long StatementCount { get { return Buffer().StatementCount; } }
		public MemoryStore Load() { return Buffer(); }
		private MemoryStore Buffer() {
			if (ms != null) return ms;
			ms = new MemoryStore();
			ms.allowIndexing = false;
			Select(ms);
			return ms;
		}
		
		internal class Single : SelectResult {
			Store source;
			Statement template;
			public Single(Store source, Statement template) {
				this.source = source;
				this.template = template;
			}
			public override void Select(StatementSink sink) {
				source.Select(template, sink);
			}
		}
		
		internal class Multi : SelectResult {
			Store source;
			Statement[] templates;
			public Multi(Store source, Statement[] templates) {
				this.source = source;
				this.templates = templates;
			}
			public override void Select(StatementSink sink) {
				source.Select(templates, sink);
			}
		}
	}
}

namespace SemWeb.Stores {

	public class MultiStore : Store {
		ArrayList stores = new ArrayList();
		
		public MultiStore() { }
		
		public void Add(Store store) {
			stores.Add(store);
		}
		
		public void Add(Store store, RdfReader source) {
			Add(store);
			store.Import(source);
		}
		
		public void Remove(Store store) {
			stores.Remove(store);
		}
		
		public override int StatementCount {
			get {
				int ret = 0;
				foreach (Store s in stores)
					ret += s.StatementCount;
				return ret;
			}
		}

		public override void Clear() {
			throw new InvalidOperationException("Clear is not a valid operation on a MultiStore.");
		}
		
		public override Entity[] GetAllEntities() {
			Hashtable h = new Hashtable();
			foreach (Store s in stores)
				foreach (Resource r in s.GetAllEntities())
					h[r] = h;
			return (Entity[])new ArrayList(h.Keys).ToArray(typeof(Entity));
		}
		
		public override Entity[] GetAllPredicates() {
			Hashtable h = new Hashtable();
			foreach (Store s in stores)
				foreach (Resource r in s.GetAllPredicates())
					h[r] = h;
			return (Entity[])new ArrayList(h.Keys).ToArray(typeof(Entity));
		}

		public override Entity[] GetAllMetas() {
			Hashtable h = new Hashtable();
			foreach (Store s in stores)
				foreach (Resource r in s.GetAllMetas())
					h[r] = h;
			return (Entity[])new ArrayList(h.Keys).ToArray(typeof(Entity));
		}

		public override void Add(Statement statement) { throw new InvalidOperationException("Add is not a valid operation on a MultiStore."); }
		
		public override bool Contains(Statement statement) {
			foreach (Store s in stores)
				if (s.Contains(statement))
					return true;
			return false;
		}
			
		public override void Remove(Statement statement) { throw new InvalidOperationException("Add is not a valid operation on a MultiStore."); }
		
		public override void Select(Statement template, StatementSink result) {
			foreach (Store s in stores)
				s.Select(template, result);
		}
		
		public override void Select(Statement[] templates, StatementSink result) {
			foreach (Store s in stores)
				s.Select(templates, result);
		}

		public override void Replace(Entity a, Entity b) {
			foreach (Store s in stores)
				s.Replace(a, b);
		}
		
		public override void Replace(Statement find, Statement replacement) {
			foreach (Store s in stores)
				s.Replace(find, replacement);
		}
		
		public override Entity[] FindEntities(Statement[] filters) {
			Hashtable h = new Hashtable();
			foreach (Store s in stores)
				foreach (Entity e in s.FindEntities(filters))
					h[e] = h;
			return (Entity[])new ArrayList(h.Keys).ToArray(typeof(Entity));
		}
		
	}
	
}
