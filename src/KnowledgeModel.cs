using System;
using System.Collections;
using System.IO;
using System.Text;

namespace SemWeb {

	public class KnowledgeModel : Store {
		
		MultiStore stores;
		Store mainstore;
		
		public KnowledgeModel() : base(null) {
			stores = new MultiStore(this);
			mainstore = stores;
		}
		
		public KnowledgeModel(Store store) : this() {
			stores.Add(store);
		}
		
		public KnowledgeModel(RdfParser parser) : this() {
			stores.Add(new MemoryStore(parser, this));
		}

		public override KnowledgeModel Model { get { return this; } }
		
		public MultiStore Storage { get { return stores; } }
		
		public void Add(Store storage) {
			Storage.Add(storage);
		}
		
		public void AddReasoning(ReasoningEngine engine) {
			mainstore = new InferenceStore(mainstore, engine);
		}
		
		public Entity this[string uri] {
			get {
				return GetResource(uri);
			}
		}
		
		public override Entity GetResource(string uri, bool create) {
			return stores.GetResource(uri, create);
		}
		
		public override void Select(Statement template, StatementSink result) {
			mainstore.Select(template, result);
		}
		
		public override void Select(Statement[] templates, StatementSink result) {
			mainstore.Select(templates, result);
		}

		public override int StatementCount { get { return stores.StatementCount; } }

		public override void Clear() { throw new InvalidOperationException(); }
		public override Entity CreateAnonymousResource() { throw new InvalidOperationException(); }
		public override void Add(Statement statement) { throw new InvalidOperationException(); }
		public override void Remove(Statement statement) { throw new InvalidOperationException(); }
	}

		
}
