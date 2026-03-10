import { useEffect, useState, useCallback } from 'react';
import { api, WorkflowSummary, ExecutionSummary, Event as DfEvent } from './api';

type Tab = 'workflows' | 'executions' | 'events';

export default function App() {
  const [tab, setTab] = useState<Tab>('workflows');
  const [health, setHealth] = useState<string>('...');
  const [workflows, setWorkflows] = useState<WorkflowSummary[]>([]);
  const [executions, setExecutions] = useState<ExecutionSummary[]>([]);
  const [events, setEvents] = useState<DfEvent[]>([]);
  const [showCreate, setShowCreate] = useState(false);

  const refresh = useCallback(async () => {
    try {
      const [h, w, e, x] = await Promise.all([
        api.health(),
        api.workflows.list(),
        api.events.list(),
        api.executions.list(),
      ]);
      setHealth(h);
      setWorkflows(w);
      setEvents(e);
      setExecutions(x);
    } catch { setHealth('Unhealthy'); }
  }, []);

  useEffect(() => { refresh(); const i = setInterval(refresh, 5000); return () => clearInterval(i); }, [refresh]);

  return (
    <div className="app">
      <header>
        <h1>⚡ DotnetFlow</h1>
        <span className={`health-badge ${health === 'Healthy' ? 'healthy' : 'unhealthy'}`}>{health}</span>
      </header>

      <div className="tabs">
        {(['workflows', 'executions', 'events'] as Tab[]).map(t => (
          <button key={t} className={`tab ${tab === t ? 'active' : ''}`} onClick={() => setTab(t)}>
            {t.charAt(0).toUpperCase() + t.slice(1)} ({t === 'workflows' ? workflows.length : t === 'executions' ? executions.length : events.length})
          </button>
        ))}
      </div>

      {tab === 'workflows' && (
        <>
          <div className="flex justify-between items-center" style={{ marginBottom: '1rem' }}>
            <h2 style={{ fontSize: '1.1rem' }}>Workflows</h2>
            <button className="btn btn-primary" onClick={() => setShowCreate(!showCreate)}>
              {showCreate ? 'Cancel' : '+ New Workflow'}
            </button>
          </div>
          {showCreate && <CreateWorkflow onCreated={() => { setShowCreate(false); refresh(); }} />}
          {workflows.length === 0 ? (
            <div className="empty">No workflows yet. Create one to get started.</div>
          ) : (
            workflows.map(w => (
              <div key={w.id} className="card">
                <div className="flex justify-between items-center">
                  <h3>{w.name}</h3>
                  <div className="flex gap-1">
                    <button className="btn btn-ghost" onClick={async () => { await api.executions.start(w.id); refresh(); }}>▶ Run</button>
                    <button className="btn btn-danger" onClick={async () => { await api.workflows.delete(w.id); refresh(); }}>Delete</button>
                  </div>
                </div>
                <p className="meta">{w.description}</p>
                <p className="meta mt-1">{w.stepCount} steps · {w.triggerCount} triggers · {w.isActive ? '🟢 Active' : '🔴 Inactive'}</p>
              </div>
            ))
          )}
        </>
      )}

      {tab === 'executions' && (
        <>
          <h2 style={{ fontSize: '1.1rem', marginBottom: '1rem' }}>Executions</h2>
          {executions.length === 0 ? (
            <div className="empty">No executions yet. Run a workflow to see results.</div>
          ) : (
            executions.map(x => (
              <div key={x.id} className="card">
                <div className="flex justify-between items-center">
                  <div>
                    <h3>{x.workflowName ?? 'Unknown Workflow'}</h3>
                    <p className="meta">Step {x.currentStepIndex}/{x.totalSteps} · Started {new Date(x.startedAt).toLocaleString()}</p>
                  </div>
                  <div className="flex gap-1 items-center">
                    <span className={`status-badge status-${x.status}`}>{x.status}</span>
                    {(x.status === 'Running' || x.status === 'Pending') && (
                      <button className="btn btn-danger" onClick={async () => { await api.executions.cancel(x.id); refresh(); }}>Cancel</button>
                    )}
                  </div>
                </div>
              </div>
            ))
          )}
        </>
      )}

      {tab === 'events' && (
        <>
          <div className="flex justify-between items-center" style={{ marginBottom: '1rem' }}>
            <h2 style={{ fontSize: '1.1rem' }}>Events</h2>
          </div>
          <PublishEvent onPublished={refresh} />
          {events.length === 0 ? (
            <div className="empty">No events yet.</div>
          ) : (
            events.map(e => (
              <div key={e.id} className="card">
                <div className="flex justify-between items-center">
                  <h3>{e.type}</h3>
                  <span className={`status-badge ${e.processed ? 'status-Completed' : 'status-Pending'}`}>
                    {e.processed ? 'Processed' : 'Pending'}
                  </span>
                </div>
                <p className="meta">Source: {e.source} · {new Date(e.occurredAt).toLocaleString()}</p>
                <pre style={{ fontSize: '0.75rem', marginTop: '0.5rem', color: 'var(--text-dim)', overflow: 'auto' }}>{e.payload}</pre>
              </div>
            ))
          )}
        </>
      )}
    </div>
  );
}

function CreateWorkflow({ onCreated }: { onCreated: () => void }) {
  const [name, setName] = useState('');
  const [desc, setDesc] = useState('');
  const [steps, setSteps] = useState([{ name: 'Step 1', type: 'action', order: 0 }]);

  const addStep = () => setSteps([...steps, { name: `Step ${steps.length + 1}`, type: 'action', order: steps.length }]);

  const submit = async () => {
    if (!name.trim()) return;
    await api.workflows.create({ name, description: desc, steps });
    onCreated();
  };

  return (
    <div className="card" style={{ marginBottom: '1rem' }}>
      <div className="form-group">
        <label>Name</label>
        <input value={name} onChange={e => setName(e.target.value)} placeholder="My Workflow" />
      </div>
      <div className="form-group">
        <label>Description</label>
        <textarea value={desc} onChange={e => setDesc(e.target.value)} placeholder="What does this workflow do?" />
      </div>
      <div className="form-group">
        <label>Steps</label>
        <ul className="step-list">
          {steps.map((s, i) => (
            <li key={i} className="step-item">
              <span><span className="step-order">#{s.order + 1}</span> {s.name}</span>
              <span className="step-type">{s.type}</span>
            </li>
          ))}
        </ul>
        <button className="btn btn-ghost mt-1" onClick={addStep}>+ Add Step</button>
      </div>
      <button className="btn btn-primary" onClick={submit}>Create Workflow</button>
    </div>
  );
}

function PublishEvent({ onPublished }: { onPublished: () => void }) {
  const [type, setType] = useState('');
  const [payload, setPayload] = useState('{}');
  const [source, setSource] = useState('ui');

  const submit = async () => {
    if (!type.trim()) return;
    await api.events.publish({ type, payload, source });
    setType('');
    setPayload('{}');
    onPublished();
  };

  return (
    <div className="card" style={{ marginBottom: '1rem' }}>
      <h3 style={{ marginBottom: '0.75rem' }}>Publish Event</h3>
      <div className="grid-2">
        <div className="form-group">
          <label>Type</label>
          <input value={type} onChange={e => setType(e.target.value)} placeholder="order.placed" />
        </div>
        <div className="form-group">
          <label>Source</label>
          <input value={source} onChange={e => setSource(e.target.value)} placeholder="ui" />
        </div>
      </div>
      <div className="form-group">
        <label>Payload (JSON)</label>
        <textarea value={payload} onChange={e => setPayload(e.target.value)} />
      </div>
      <button className="btn btn-primary" onClick={submit}>Publish</button>
    </div>
  );
}
