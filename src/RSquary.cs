using System;
using System.Collections;

using SemWeb;

namespace SemWeb.Query {
	public class QueryException : ApplicationException {
		public QueryException(string message, Exception cause) : base(message, cause) {
		}
	}
	
	public class RSquary {
		
		// TODO: Optional statements
		// TODO: Grouping and disjunctions
		
		public static Entity qSelect = "http://purl.oclc.org/NET/rsquary/select";
		public static Entity qVariable = "http://purl.oclc.org/NET/rsquary/variable";
		public static Entity qLimit = "http://purl.oclc.org/NET/rsquary/returnLimit";
		public static Entity qStart = "http://purl.oclc.org/NET/rsquary/returnStart";
		public static Entity qDistinctFrom = "http://purl.oclc.org/NET/rsquary/distinctFrom";
		
		KnowledgeModel model;
		MemoryStore store;
		Entity query;
		
		Hashtable variableHash = new Hashtable();
		VariableBinding[] initialBindings;
		ArrayList[,] predicateFilters;
		ArrayList[] valueFilters;
		ArrayList[] variablesDistinct;
		VariableNode rootVariable;			
		
		int start = -1;
		int limit = -1;
		
		private class VariableNode {
			public Resource Variable;
			public int BindingIndex;
			
			public Set Dependencies = new Set();
			
			public VariableNode Parent = null;
			public ArrayList Children = new ArrayList();
			
			public bool Sees(VariableNode dependency) {
				if (this == dependency) return true;
				if (Parent == null || Parent == this) return false;
				if (Parent == dependency) return true;
				return Parent.Sees(dependency);
			}
		}
		
		private class SearchState {
			public Hashtable cachedSelections = new Hashtable();
			public Store target;
			public QueryResultSink sink;
			public VariableBinding[] bindings;
			public Set[] alternatives;
			public Hashtable predicateFilterCache;
			public ArrayList[] predicateFilterCacheClear;
			public int count = 0;
			
			public Hashtable targetQueryObject = new Hashtable();
		}	
		
		private bool IsQueryPredicate(Entity e) {
			if (e == qSelect) return true;
			if (e == qVariable) return true;
			if (e == qDistinctFrom) return true;
			if (e == qLimit) return true;
			if (e == qStart) return true;
			return false;
		}
		
		public RSquary(Store queryModel, string queryUri) : this(queryModel, queryUri, null) {
		}
		
		public RSquary(Store queryModel, string queryUri, Hashtable extraValueFilters) {			
			model = new KnowledgeModel();
			store = new MemoryStore(model);
			model.Add(store);
			store.Import(queryModel);
			
			query = model.GetResource(queryUri);

			start = GetIntOption(qStart);
			limit = GetIntOption(qLimit);
			
			// Get the list of variables
			
			ArrayList variables = new ArrayList();
			foreach (Resource r in query[qSelect]) {
				if (!(r is Entity)) throw new ArgumentException("Query variables cannot be literals.");
				variables.Add(new VariableBinding((Entity)r, null, false));
			}
			foreach (Resource r in query[qVariable]) {
				if (!(r is Entity)) throw new ArgumentException("Query variables cannot be literals.");
				variables.Add(new VariableBinding((Entity)r, null, true));
			}
			initialBindings = (VariableBinding[])variables.ToArray(typeof(VariableBinding));
			
			foreach (VariableBinding b in initialBindings)
				variableHash[b.Variable] = b.Variable;
			
			// Get a list of all statements about each variable.
			
			predicateFilters = new ArrayList[initialBindings.Length,2];
			valueFilters = new ArrayList[initialBindings.Length];
			variablesDistinct = new ArrayList[initialBindings.Length];
			
			int[] constantFilters = new int[initialBindings.Length];
			
			for (int i = 0; i < initialBindings.Length; i++) {
				Entity variable = initialBindings[i].Variable;

				valueFilters[i] = new ArrayList();
				
				for (int forward = 0; forward <= 1; forward++) {
					predicateFilters[i,forward] = model.Select(new Statement((forward==1) ? variable : null, null, (forward==1) ? null : variable)).Statements;
					
					// Remove any query predicates and value filters
					for (int z = 0; z < predicateFilters[i,forward].Count; z++) {
						Statement statement = (Statement)predicateFilters[i,forward][z];
						
						ValueFilter f = null;
						if (extraValueFilters != null)
							f = extraValueFilters[statement.Predicate.Uri] as ValueFilter;
						if (f == null)
							f = ValueFilter.GetValueFilter(statement.Predicate, (forward == 1) ? statement.Object : statement.Subject);
						
						if (f != null)
							valueFilters[i].Add(f);
							
						if (f != null || IsQueryPredicate(statement.Predicate)) {
							predicateFilters[i,forward].RemoveAt(z);
							z--;
							continue;
						}
						
						// Increment the count of filters based only on constants.
						if (!variableHash.ContainsKey(statement.Predicate) && (forward == 1 ? !variableHash.ContainsKey(statement.Object) : !variableHash.ContainsKey(statement.Subject)))
							constantFilters[i]++;
					}

				}
				
				variablesDistinct[i] = SymmetricSelect(variable, qDistinctFrom, model);
			}
			
			// Create variable nodes for each variable.
			
			VariableNode[] variableNodes = new VariableNode[initialBindings.Length];
			for (int v = 0; v < initialBindings.Length; v++) {
				variableNodes[v] = new VariableNode();
				variableNodes[v].Variable = initialBindings[v].Variable;
				variableNodes[v].BindingIndex = v;
				variableHash[variableNodes[v].Variable] = variableNodes[v];
			}
			
			// Find the dependencies among the nodes.
			
			for (int v = 0; v < initialBindings.Length; v++) {
				for (int forward = 0; forward <= 1; forward++) {
					foreach (Statement s in predicateFilters[v, forward]) {
						if (variableHash.ContainsKey(s.Predicate))
							variableNodes[v].Dependencies.Add( variableHash[s.Predicate] );
						Resource obj = forward == 1 ? s.Object : s.Subject;
						if (variableHash.ContainsKey(obj))
							variableNodes[v].Dependencies.Add( variableHash[obj] );
					}
				}
				
			}
			
			// Choose a root variable.  This should have the most number of
			// filters tied to constants (non-variable resources), with
			// ties going to selection variables.
			
			foreach (VariableNode v in variableNodes) {
				if (rootVariable == null) { rootVariable = v; continue; }
				if (constantFilters[rootVariable.BindingIndex] < constantFilters[v.BindingIndex])
					rootVariable = v;
				if (constantFilters[rootVariable.BindingIndex] == constantFilters[v.BindingIndex]
					 && initialBindings[rootVariable.BindingIndex].var && !initialBindings[v.BindingIndex].var)
					rootVariable = v;
			}
			rootVariable.Parent = rootVariable;
			
			// Hook the remaining variables into the tree somewhere
			
			for (int v = 1; v < initialBindings.Length; v++) {
				// Choose the next variable to place into the tree.
				// It will be the variable with the most number of
				// its dependencies already placed on the tree, with
				// ties going to selection variables.
				VariableNode node = null;
				int dc = -1;
				foreach (VariableNode n in variableNodes) {
					if (n.Parent != null) continue;
					
					int c = 0;
					foreach (VariableNode dep in n.Dependencies.Items())
						if (dep.Parent != null)
							c++;
					
					if (node == null || c > dc) { node = n; dc = c; continue; }
					if (c == dc && initialBindings[node.BindingIndex].var && !initialBindings[n.BindingIndex].var) { node = n; dc = c; continue; }
				}
				
				// Find a place for this variable on the tree.  Place
				// it as a child of any of its dependencies, if that
				// dependency can see all of its other dependencies
				// that are already on the tree.
				
				foreach (VariableNode dep in node.Dependencies.Items()) {
					if (dep.Parent == null) continue;
					bool seesAll = true;
					foreach (VariableNode dep2 in node.Dependencies.Items()) {
						if (dep2.Parent == null) continue;
						if (!dep.Sees(dep2)) { seesAll = false; break; }
					}
					if (!seesAll) continue;
					
					node.Parent = dep;
					break;
				}
				if (node.Parent != null) continue; // done with this one
				
				// This variable is dependent on multiple nodes of the tree.
				// Find the first node it's dependent on, and then move
				// subsequent dependencies into subparts of that node.
				// This could be done better...
				VariableNode deproot = null;
				foreach (VariableNode dep in node.Dependencies.Items()) {
					if (dep.Parent == null) continue;
					if (deproot == null) { deproot = dep; continue; }
					
					// Move other dependencies within this node
					VariableNode p = dep;
					while (p.Parent != p) {
						if (deproot.Sees(p.Parent)) {
							p.Parent = deproot;
							deproot = p.Parent;
							break;
						}
						p = p.Parent;
					}
				}			
				node.Parent = deproot;
				if (node.Parent != null) continue; // done with this one
				
				if (node.Parent == null)
					throw new ArgumentException("Query variable " + node.Variable + " is not connected to the other entities in the query.");
			}
			
			// Find the children of each variable
			foreach (VariableNode v in variableNodes)
				if (v.Parent != v)
					v.Parent.Children.Add(v);
		}
		
		private int GetIntOption(Entity predicate) {
			Resource[] rr = query[predicate];
			if (rr.Length == 0) return -1;
			Resource r = rr[0];
			if (r == null || !(r is Literal)) return -1;
			try {
				return int.Parse(((Literal)r).Value);
			} catch (Exception e) {
				throw new QueryException("Invalid integer value for <" + predicate + ">, '" + ((Literal)r).Value + "'.", e);
			}
		}
		
		private Resource GetBinding(Resource ent, VariableNode currentNode, bool reqEntity, SearchState state) {
			if (!(ent is Entity)) {
				if (reqEntity) return null;
				return ent;
			}
			
			if (ent.Uri == null)
				throw new ArgumentException("Anonymous nodes cannot appear in queries.");

			if (variableHash.ContainsKey(ent)) {
				while (currentNode.Parent != currentNode) {
					currentNode = currentNode.Parent;
					if (currentNode.Variable.Equals(ent)) { 
						if ((!reqEntity || state.bindings[currentNode.BindingIndex].Target is Entity))
							return state.bindings[currentNode.BindingIndex].Target;
						else
							return null;
					}
				}
				return null;
			}
			
			Resource ret = (Resource)state.targetQueryObject[ent.Uri];
			if (ret == null) {
				ret = state.target.GetResource(ent.Uri);
				state.targetQueryObject[ent.Uri] = ret;
			}
			if (reqEntity && !(ret is Entity)) return null;
			return ret;
		}

		/*private Set GetAlternativesSet(Resource ent, int varIndex, SearchState state, SelectCacheKey key) {
			if (!(ent is Entity)) return null;
			
			for (int i = 0; i < varIndex; i++)
				if (state.bindings[i].Variable.Equals(ent)) {
					state.predicateFilterCacheClear[i].Add(key);
					return state.alternatives[i];
				}
			
			return null;
		}*/

		public void Query(Store target, QueryResultSink sink) {
			if (start != -1 || limit != -1)
				sink = new StartLimitSink(start, limit, sink);
			
			SearchState state = new SearchState();
			state.target = target;
			state.sink = sink;
			state.bindings = (VariableBinding[])initialBindings.Clone();
			state.alternatives = new Set[state.bindings.Length];
			state.predicateFilterCache = new Hashtable();
			
			state.predicateFilterCacheClear = new ArrayList[state.bindings.Length];
			for (int i = 0; i < state.bindings.Length; i++)
				state.predicateFilterCacheClear[i] = new ArrayList();
			
			Recurse(state, rootVariable);
		}
		
		public bool Test(Store target) {
			StopOnFirst sink = new StopOnFirst();
			Query(target, sink);
			return sink.Found;
		}		
		
		private bool Recurse(SearchState state, VariableNode node) {
			Set resources = null;
			
			bool forward = true;
			while (true) {
				foreach (Statement s in predicateFilters[node.BindingIndex, forward ? 1 : 0]) {
					Entity filterPredicate = (Entity)GetBinding(s.Predicate, node, true, state);
					Resource filterObject = GetBinding(forward ? s.Object : s.Subject, node, !forward, state);

					/*SelectCacheKey selectCacheKey = new SelectCacheKey(s, varIndex);
					Store selectCache = (MemoryStore)state.predicateFilterCache[selectCacheKey];
					
					if (selectCache == null) {
						// See if we want to precache results for other alternative
						// choices of variables for previously selected variables.
						Set altPred = GetAlternativesSet(s.Predicate, varIndex, state, selectCacheKey);
						Set altObj = GetAlternativesSet(forward ? s.Object : s.Subject, varIndex, state, selectCacheKey);
						
						// There are alternatives to precache.
						if (altPred != null || altObj != null) {
							// Get sets for the predicate and object.  If there aren't
							// alternatives for one, use the single target object.
							Set altPred2 = altPred, altObj2 = altObj;
							if (altPred2 == null) { altPred2 = new Set(); altPred2.Add(filterPredicate); }
							if (altObj2 == null) { altObj2 = new Set(); altObj2.Add(filterObject); }
							
							// Get an array of statements that match the query.
							ArrayList queries = new ArrayList();
							foreach (Resource p in altPred2.Items()) {
								if (!(p is Entity)) continue;
								foreach (Resource o in altObj2.Items()) {
									if (!forward && !(o is Entity)) continue;
									Statement qq = new Statement(forward ? null : (Entity)o, (Entity)p, forward ? o : null);
									queries.Add(qq);
								}
							}
							
							Console.WriteLine(s);
							selectCache = new MemoryStore(null);
							state.target.Select((Statement[])queries.ToArray(typeof(Statement)), selectCache);
							
							state.predicateFilterCache[selectCacheKey] = selectCache;
						}
					}*/
					
					Statement q = new Statement(forward ? null : (Entity)filterObject, filterPredicate, forward ? filterObject : null);
					
					if (resources == null) {
						// Select all resources that match this statement
						// The selection will also apply any value filters for this variable
						resources = SelectCache(q, forward, state, null, node).Clone();
						
						// Apply distinctFrom restrictions to make sure the value of this
						// variable is not equal to the value of other variables, as specified.
						foreach (Resource r in variablesDistinct[node.BindingIndex]) {
							Resource rb = GetBinding(r, node, false, state);
							if (rb != null) resources.Remove(rb);
						}
						
					} else {
						// Filter out resources that don't match the pattern
						if (filterPredicate == null || filterObject == null) continue;
						
						if (filterObject is Literal || resources.Size() < 5) {
							// Check that each resource matches the filter
							foreach (Resource r in resources.Items()) {
								if ((forward && !(r is Entity)) || !state.target.Contains(new Statement(forward ? (Entity)r : (Entity)filterObject, filterPredicate, forward ? filterObject : r)))
									resources.Remove(r);
							}
						} else {
							// Find all items that satisfy the filter, and then intersect the sets.
							Set filter = SelectCache(q, forward, state, null, null);
							
							// Perform the loop on the items of the smaller set.
							Set smaller = (filter.Size() < resources.Size()) ? filter : resources;
							Set larger = (filter.Size() < resources.Size()) ? resources : filter;						
							foreach (Resource r in smaller.Items())
								if (!larger.Contains(r))
									smaller.Remove(r);							
							resources = smaller;
						}
					}
				}
				
				if (forward) forward = false;
				else break;
			}
			
			if (resources == null) {
				// There were no statements about this variable.  Set it
				// to an anonymous node, since it may have any value at all.
				resources = new Set();
				resources.Add(new AnonymousNode());
			}
			
			/*foreach (SelectCacheKey predicateFilterCacheKey in state.predicateFilterCacheClear[varIndex])
				state.predicateFilterCache.Remove(predicateFilterCacheKey);
			state.predicateFilterCacheClear[varIndex].Clear();
			
			state.alternatives[varIndex] = resources;*/
			
			if (node.Children.Count == 0) {
				// This is the end of a tree branch.
				foreach (Resource r in resources.Items()) {
					state.bindings[node.BindingIndex].Target = r;
					if (!state.sink.Add(state.bindings)) return true;
				}
				return false;
			
			} else {
				// Recurse on each possible value for this node for the first
				// child.
				
				VariableNode firstchild = (VariableNode)node.Children[0];
				QueryResultSink oldsink = state.sink;
				
				if (node.Children.Count > 1)
					state.sink = new BufferSink();
				
				foreach (Resource r in resources.Items()) {
					state.bindings[node.BindingIndex].Target = r;
					if (Recurse(state, firstchild)) return true;
				}
				
				// For subsequent children, except the last, permute on the
				// buffered bindings.
				foreach (VariableNode child in node.Children) {
					if (child == firstchild) continue;
					ArrayList bindings = (ArrayList)((BufferSink)state.sink).Bindings;
					
					if (child == node.Children[node.Children.Count-1]) {
						state.sink = oldsink;
					} else {
						state.sink = new BufferSink();
					}
					
					foreach (VariableBinding[] binding in bindings) {
						state.bindings = binding;
						if (Recurse(state, child)) return true;
					}
				}
			}
			
			return false;
		}
		
		private ArrayList SymmetricSelect(Entity e, Entity p, Store model) {
			ArrayList ret = new ArrayList();
			model.Select(new Statement(e, p, null), new PutInArraySink(false, ret));
			model.Select(new Statement(null, p, e), new PutInArraySink(true, ret));
			return ret;
		}
		
		private class SelectCacheKey {
			public Statement Q;
			public VariableNode Var;
			
			public SelectCacheKey(Statement q, VariableNode var) {
				Q = q;
				Var = var;
			}
			
			public override int GetHashCode() { return Q.GetHashCode(); }
			public override bool Equals(object other) {
				return ((SelectCacheKey)other).Var == Var && Q.Equals(((SelectCacheKey)other).Q);
			}
		}
		
		private Set SelectCache(Statement q, bool forward, SearchState state, Store queryTarget, VariableNode var) {
			if (var != null && valueFilters[var.BindingIndex].Count == 0)
				var = null;
			
			if (queryTarget == null)
				queryTarget = state.target;
			
			SelectCacheKey key = new SelectCacheKey(q, var);
			
			Set cached = (Set)state.cachedSelections[key];
			if (cached == null) {
				cached = new Set();
				queryTarget.Select(q, new PutInSet(forward, cached));
				
				// Do value filters
				if (var != null)
					ApplyValueFilters(cached, valueFilters[var.BindingIndex], state.target);
							
				state.cachedSelections[key] = cached;
			}
			return cached;
		}
		
		private void ApplyValueFilters(Set resources, ArrayList valueFilters, Store target) {
			foreach (ValueFilter f in valueFilters) {
				foreach (Resource r in resources.Items()) {
					if (!(r is Literal) && f is LiteralValueFilter)
						resources.Remove(r);
					else if (!f.Filter(r, target))
						resources.Remove(r);
				}
			}
		}
		
		private class PutInArraySink : StatementSink {
			Hashtable seen = new Hashtable();
			bool subject;
			ArrayList sink;
			
			public PutInArraySink(bool subject, ArrayList sink) {
				this.subject = subject; this.sink = sink;
			}

			public bool Add(Statement statement) {
				Resource r;
				if (subject) r = statement.Subject;
				else r = statement.Object;
				if (!(r.Uri != null || r is Literal) || !seen.ContainsKey(r)) {
					sink.Add(r);
					if ((r.Uri != null || r is Literal))
						seen[r] = r;
				}
				return true;
			}
		}
		
		private class PutInSet : StatementSink {
			bool subject;
			Set sink;
			
			public PutInSet(bool subject, Set sink) {
				this.subject = subject; this.sink = sink;
			}

			public bool Add(Statement statement) {
				if (subject) sink.Add(statement.Subject);
				else sink.Add(statement.Object);
				return true;
			}
		}

		private class StatementExistsSink2 : StatementSink {
			bool exists = false;
			
			public bool Exists { get { return exists; } }
			
			public bool Add(Statement statement) {
				if (statement.Predicate == qDistinctFrom) return true;
				exists = true;
				return false;
			}
		}
		
		private class Set {
			Hashtable store;
			ArrayList items;
			
			public Set() { store = new Hashtable(); }
			public Set(Set other) { store = (Hashtable)other.store.Clone(); }
			
			public void Add(object r) { store[r] = r; items = null; }
			public void Remove(object r) { store.Remove(r); items = null; }
			public bool Contains(object r) { return store.ContainsKey(r); }
			
			public int Size() { return store.Count; }
			
			public ICollection Items() {
				if (items == null) {
					items = new ArrayList();
					items.AddRange(store.Keys);
				}
				return items;
			}
			
			public Set Clone() { return new Set(this); }
		}
			
		private class StopOnFirst : QueryResultSink {
			public bool Found = false;
			public override bool Add(VariableBinding[] result) {
				Found = true;
				return false;
			}
		}
	
		private class BufferSink : QueryResultSink {
			public ArrayList Bindings = new ArrayList();
			public override bool Add(VariableBinding[] result) {
				Bindings.Add(result.Clone());
				return true;
			}
		}

		private class StartLimitSink : QueryResultSink {
			int start, limit;
			QueryResultSink sink;
			int counter = 0;
			public StartLimitSink(int start, int limit, QueryResultSink sink) { this.start = start; this.limit = limit; this.sink = sink; }
			public override bool Add(VariableBinding[] result) {
				counter++;
				if (counter >= start || start == -1) sink.Add(result);
				if (counter == limit) return false;
				return true;
			}
		}
	}
	
	public class PrintQuerySink : QueryResultSink {
		public override bool Add(VariableBinding[] result) {
			foreach (VariableBinding var in result)
				Console.WriteLine(var.Variable + " ==> " + var.Target);
			Console.WriteLine();
			return true;
		}
	}
	
	public abstract class QueryResultSink {
		public abstract bool Add(VariableBinding[] result);
	}
	
	public struct VariableBinding {
		Entity v;
		Resource t;
		internal bool var;
		
		internal VariableBinding(Entity variable, Resource target, bool isvar) {
			v = variable;
			t = target;
			var = isvar;
		}
		
		public Entity Variable { get { return v; } set { v = value; } }
		public Resource Target { get { return t; } set { t = value; } }
	}
}	

