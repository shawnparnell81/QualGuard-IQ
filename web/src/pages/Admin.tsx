import { useState } from 'react';
import { Navigate } from 'react-router-dom';
import { Alert, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Pagination, Paper, Stack, Switch, TextField, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import type { ColumnDef } from '@tanstack/react-table';
import { del, get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { fmtDateTime } from '../components/Status';
import { mono } from '../theme';

const mod = modules.find((m) => m.key === 'admin')!;
interface Site { id: string; code: string; name: string; address?: string | null; timeZone: string; isArchived: boolean; departments: { id: string; code: string; name: string }[] }
interface Role { id: string; name: string; description: string | null; isSystem: boolean; permissions: string[]; rowVersion: number }
interface User { id: string; email: string; displayName: string; isActive: boolean; isLockedOut: boolean; lastSignInAt: string | null; roles: { id: string; roleId: string; roleName: string; siteId: string | null; departmentId: string | null }[] }

const useSites = () => useQuery({ queryKey: ['sites'], queryFn: () => get<Site[]>('/api/organization/sites') });
const Err = ({ e }: { e: string | null }) => (e ? <Alert severity="error" sx={{ mb: 2 }}>{e}</Alert> : null);

export function AdminHome() {
  const { can } = useAuth();
  const first = mod.sidebar.flatMap((s) => s.children ?? [s]).find((i) => !i.permission || can(i.permission));
  return <Navigate to={first?.path ?? '/'} replace />;
}

const userSchema = z.object({ email: z.string().email(), displayName: z.string().trim().min(1).max(200), password: z.string().min(12, 'At least 12 characters') });

export function UsersPage() {
  const qc = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [assignFor, setAssignFor] = useState<User | null>(null);
  const [assign, setAssign] = useState({ roleId: '', siteId: '', departmentId: '' });
  const users = useQuery({ queryKey: ['users'], queryFn: () => get<User[]>('/api/admin/users') });
  const roles = useQuery({ queryKey: ['roles'], queryFn: () => get<Role[]>('/api/admin/roles') });
  const sites = useSites();
  const siteName = (id: string | null) => (id ? sites.data?.find((s) => s.id === id)?.code ?? '?' : 'All sites');
  const deptName = (sid: string | null, did: string | null) => (did ? sites.data?.find((s) => s.id === sid)?.departments.find((d) => d.id === did)?.code : null);
  const { register, handleSubmit, reset, formState: { errors } } = useForm<z.infer<typeof userSchema>>({ resolver: zodResolver(userSchema) });
  const run = async (f: () => Promise<unknown>) => { setError(null); try { await f(); qc.invalidateQueries({ queryKey: ['users'] }); return true; } catch (e) { setError((e as Error).message); return false; } };
  const cols: ColumnDef<User, any>[] = [
    { header: 'Name', accessorKey: 'displayName' },
    { header: 'Email', accessorKey: 'email' },
    { header: 'Roles', id: 'roles', cell: (c) => (
      <Stack direction="row" gap={0.5} flexWrap="wrap">
        {c.row.original.roles.map((r) => <Chip key={r.id} size="small" label={`${r.roleName} · ${siteName(r.siteId)}${deptName(r.siteId, r.departmentId) ? '/' + deptName(r.siteId, r.departmentId) : ''}`}
          onDelete={() => run(() => del(`/api/admin/users/${c.row.original.id}/roles/${r.id}`))} />)}
        <Chip size="small" variant="outlined" label="+ Role" onClick={() => { setAssignFor(c.row.original); setAssign({ roleId: '', siteId: '', departmentId: '' }); }} />
      </Stack>) },
    { header: 'Status', id: 'st', cell: (c) => <Stack direction="row" gap={0.5}>{c.row.original.isActive ? <Chip size="small" color="success" label="Active" /> : <Chip size="small" label="Disabled" />}{c.row.original.isLockedOut && <Chip size="small" color="error" label="Locked" />}</Stack> },
    { header: 'Last sign-in', accessorKey: 'lastSignInAt', cell: (c) => fmtDateTime(c.getValue()) },
    { header: '', id: 'ops', cell: (c) => {
      const u = c.row.original;
      return (
        <Stack direction="row">
          {u.isLockedOut && <Button size="small" onClick={() => run(() => post(`/api/admin/users/${u.id}/unlock`))}>Unlock</Button>}
          {u.isActive ? <Button size="small" color="error" onClick={() => run(() => post(`/api/admin/users/${u.id}/disable`))}>Disable</Button>
            : <Button size="small" onClick={() => run(() => post(`/api/admin/users/${u.id}/enable`))}>Enable</Button>}
          <Button size="small" onClick={() => run(() => post(`/api/admin/users/${u.id}/revoke-sessions`))}>Sign out everywhere</Button>
        </Stack>);
    } },
  ];
  const selSite = sites.data?.find((s) => s.id === assign.siteId);
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Users' }]} title="Users" actions={<Button variant="contained" onClick={() => { reset(); setAdding(true); }}>Add user</Button>}>
      <Err e={error} />
      <DataTable data={users.data ?? []} columns={cols} />
      <Dialog open={adding} onClose={() => setAdding(false)} fullWidth maxWidth="xs">
        <DialogTitle>Add user</DialogTitle>
        <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          <TextField label="Full name" {...register('displayName')} error={!!errors.displayName} />
          <TextField label="Email" {...register('email')} error={!!errors.email} helperText={errors.email?.message} />
          <TextField label="Temporary password" type="password" {...register('password')} error={!!errors.password} helperText={errors.password?.message} />
        </DialogContent>
        <DialogActions><Button onClick={() => setAdding(false)}>Cancel</Button>
          <Button variant="contained" onClick={handleSubmit(async (v) => { if (await run(() => post('/api/admin/users', v))) setAdding(false); })}>Create</Button></DialogActions>
      </Dialog>
      <Dialog open={!!assignFor} onClose={() => setAssignFor(null)} fullWidth maxWidth="xs">
        <DialogTitle>Assign role to {assignFor?.displayName}</DialogTitle>
        <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          <TextField select label="Role" value={assign.roleId} onChange={(e) => setAssign({ ...assign, roleId: e.target.value })}>
            {(roles.data ?? []).map((r) => <MenuItem key={r.id} value={r.id}>{r.name}</MenuItem>)}
          </TextField>
          <TextField select label="Site scope" value={assign.siteId} onChange={(e) => setAssign({ ...assign, siteId: e.target.value, departmentId: '' })}>
            <MenuItem value="">All sites</MenuItem>{(sites.data ?? []).map((s) => <MenuItem key={s.id} value={s.id}>{s.code} — {s.name}</MenuItem>)}
          </TextField>
          <TextField select label="Department scope" value={assign.departmentId} disabled={!selSite} onChange={(e) => setAssign({ ...assign, departmentId: e.target.value })}>
            <MenuItem value="">Whole site</MenuItem>{(selSite?.departments ?? []).map((d) => <MenuItem key={d.id} value={d.id}>{d.code} — {d.name}</MenuItem>)}
          </TextField>
        </DialogContent>
        <DialogActions><Button onClick={() => setAssignFor(null)}>Cancel</Button>
          <Button variant="contained" disabled={!assign.roleId} onClick={async () => {
            if (await run(() => post(`/api/admin/users/${assignFor!.id}/roles`, { roleId: assign.roleId, siteId: assign.siteId || null, departmentId: assign.departmentId || null }))) setAssignFor(null);
          }}>Assign</Button></DialogActions>
      </Dialog>
    </ModuleWorkspace>
  );
}

export function RolesPage() {
  const qc = useQueryClient();
  const roles = useQuery({ queryKey: ['roles'], queryFn: () => get<Role[]>('/api/admin/roles') });
  const catalog = useQuery({ queryKey: ['perm-catalog'], queryFn: () => get<string[]>('/api/admin/roles/permissions') });
  const [edit, setEdit] = useState<Partial<Role> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const groups = Object.entries((catalog.data ?? []).reduce<Record<string, string[]>>((a, p) => { (a[p.split('.')[0]] ??= []).push(p); return a; }, {}));
  const save = async () => {
    setError(null);
    try {
      const body = { name: edit!.name, description: edit!.description ?? null, permissions: edit!.permissions ?? [], rowVersion: edit!.rowVersion ?? null };
      if (edit!.id) await put(`/api/admin/roles/${edit!.id}`, body); else await post('/api/admin/roles', body);
      setEdit(null); qc.invalidateQueries({ queryKey: ['roles'] });
    } catch (e) { setError((e as Error).message); }
  };
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Roles' }]} title="Roles" actions={<Button variant="contained" onClick={() => setEdit({ name: '', permissions: [] })}>New role</Button>}>
      <Typography color="text.secondary" sx={{ mb: 2 }}>Roles are sets of permissions. A role is granted to a user for all sites, one site, or one department.</Typography>
      <DataTable data={roles.data ?? []} onRowClick={(r) => !r.isSystem && setEdit(r)} columns={[
        { header: 'Role', accessorKey: 'name', cell: (c) => <b>{c.getValue()}</b> },
        { header: 'Description', accessorKey: 'description' },
        { header: 'Permissions', accessorKey: 'permissions', cell: (c) => `${c.getValue().length} permissions` },
        { header: '', accessorKey: 'isSystem', cell: (c) => (c.getValue() ? <Chip size="small" label="Built-in" /> : null) },
      ]} />
      <Dialog open={!!edit} onClose={() => setEdit(null)} fullWidth maxWidth="md">
        <DialogTitle>{edit?.id ? `Edit ${edit.name}` : 'New role'}</DialogTitle>
        <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          <Err e={error} />
          <TextField label="Name" value={edit?.name ?? ''} onChange={(e) => setEdit({ ...edit!, name: e.target.value })} />
          <TextField label="Description" value={edit?.description ?? ''} onChange={(e) => setEdit({ ...edit!, description: e.target.value })} />
          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 1.5 }}>
            {groups.map(([g, perms]) => (
              <Paper key={g} sx={{ p: 1.5 }}>
                <Typography variant="overline">{g}</Typography>
                {perms.map((p) => (
                  <FormControlLabel key={p} sx={{ display: 'flex' }} label={<span style={{ fontFamily: mono, fontSize: 12 }}>{p}</span>}
                    control={<Checkbox size="small" checked={!!edit?.permissions?.includes(p)} onChange={(e) => setEdit({ ...edit!, permissions: e.target.checked ? [...(edit!.permissions ?? []), p] : edit!.permissions!.filter((x) => x !== p) })} />} />
                ))}
              </Paper>
            ))}
          </Box>
        </DialogContent>
        <DialogActions><Button onClick={() => setEdit(null)}>Cancel</Button><Button variant="contained" onClick={save}>Save role</Button></DialogActions>
      </Dialog>
    </ModuleWorkspace>
  );
}

export function SitesPage() {
  const qc = useQueryClient();
  const sites = useSites();
  const [error, setError] = useState<string | null>(null);
  const [site, setSite] = useState({ code: '', name: '', address: '', timeZone: 'America/New_York' });
  const [dept, setDept] = useState<{ siteId: string; code: string; name: string } | null>(null);
  const run = async (f: () => Promise<unknown>) => { setError(null); try { await f(); qc.invalidateQueries({ queryKey: ['sites'] }); return true; } catch (e) { setError((e as Error).message); return false; } };
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Sites & departments' }]} title="Sites & departments">
      <Err e={error} />
      <Paper sx={{ p: 2, mb: 3 }}>
        <Typography variant="h3" sx={{ mb: 1.5 }}>Add site</Typography>
        <Stack direction="row" gap={1}>
          <TextField size="small" label="Code" value={site.code} onChange={(e) => setSite({ ...site, code: e.target.value })} />
          <TextField size="small" label="Name" value={site.name} onChange={(e) => setSite({ ...site, name: e.target.value })} sx={{ width: 280 }} />
          <TextField size="small" label="Address" value={site.address} onChange={(e) => setSite({ ...site, address: e.target.value })} sx={{ flex: 1 }} />
          <TimeZoneSelect value={site.timeZone} onChange={(tz) => setSite({ ...site, timeZone: tz })} />
          <Button variant="contained" onClick={async () => { if (await run(() => post('/api/organization/sites', { ...site, address: site.address || null }))) setSite({ code: '', name: '', address: '', timeZone: 'America/New_York' }); }}>Add</Button>
        </Stack>
      </Paper>
      <Stack gap={2}>
        {(sites.data ?? []).map((s) => (
          <Paper key={s.id} sx={{ p: 2, opacity: s.isArchived ? 0.6 : 1 }}>
            <Stack direction="row" alignItems="center" gap={1}>
              <Typography sx={{ fontFamily: mono, fontWeight: 600 }}>{s.code}</Typography><Typography sx={{ flex: 1 }}>{s.name}</Typography>
              {!s.isArchived && <TimeZoneSelect value={s.timeZone} onChange={(tz) => run(() => put(`/api/organization/sites/${s.id}`, { code: s.code, name: s.name, address: s.address ?? null, timeZone: tz }))} />}
              {s.isArchived ? <Chip size="small" label="Archived" /> : <>
                <Button size="small" onClick={() => setDept({ siteId: s.id, code: '', name: '' })}>Add department</Button>
                <Button size="small" color="error" onClick={() => run(() => post(`/api/organization/sites/${s.id}/archive`))}>Archive</Button></>}
            </Stack>
            <Stack direction="row" gap={0.5} sx={{ mt: 1 }} flexWrap="wrap">
              {s.departments.map((d) => <Chip key={d.id} size="small" variant="outlined" label={`${d.code} — ${d.name}`} />)}
              {s.departments.length === 0 && <Typography variant="body2" color="text.secondary">No departments.</Typography>}
            </Stack>
          </Paper>
        ))}
      </Stack>
      <Dialog open={!!dept} onClose={() => setDept(null)}>
        <DialogTitle>Add department</DialogTitle>
        <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          <TextField label="Code" value={dept?.code ?? ''} onChange={(e) => setDept({ ...dept!, code: e.target.value })} />
          <TextField label="Name" value={dept?.name ?? ''} onChange={(e) => setDept({ ...dept!, name: e.target.value })} />
        </DialogContent>
        <DialogActions><Button onClick={() => setDept(null)}>Cancel</Button>
          <Button variant="contained" onClick={async () => { if (await run(() => post(`/api/organization/sites/${dept!.siteId}/departments`, { code: dept!.code, name: dept!.name }))) setDept(null); }}>Add</Button></DialogActions>
      </Dialog>
    </ModuleWorkspace>
  );
}

interface Step { order: number; name: string; requiredPermission: string; signatureMeaning: string }
interface Route { id: string; recordType: string; name: string; version: number; preventSelfApproval: boolean; steps: Step[] }
export function RoutesPage() {
  const qc = useQueryClient();
  const routes = useQuery({ queryKey: ['routes'], queryFn: () => get<Route[]>('/api/admin/config/approval-routes') });
  const catalog = useQuery({ queryKey: ['perm-catalog'], queryFn: () => get<string[]>('/api/admin/roles/permissions') });
  const [edit, setEdit] = useState<Route | null>(null);
  const [error, setError] = useState<string | null>(null);
  const setStep = (i: number, s: Partial<Step>) => setEdit({ ...edit!, steps: edit!.steps.map((x, j) => (j === i ? { ...x, ...s } : x)) });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Approval routes' }]} title="Approval routes">
      <Typography color="text.secondary" sx={{ mb: 2 }}>Each step is signed by someone holding the step's permission for the record's site. Changing a route creates a new version; approvals already in progress finish on the version they started with.</Typography>
      <DataTable data={routes.data ?? []} onRowClick={(r) => { setError(null); setEdit(structuredClone(r)); }} columns={[
        { header: 'Record type', accessorKey: 'recordType', cell: (c) => <span style={{ fontFamily: mono }}>{c.getValue()}</span> },
        { header: 'Name', accessorKey: 'name' },
        { header: 'Version', accessorKey: 'version' },
        { header: 'Steps', accessorKey: 'steps', cell: (c) => (c.getValue() as Step[]).map((s) => s.name).join(' → ') },
        { header: 'Self-approval', accessorKey: 'preventSelfApproval', cell: (c) => (c.getValue() ? 'Blocked' : 'Allowed') },
      ]} />
      <Dialog open={!!edit} onClose={() => setEdit(null)} fullWidth maxWidth="md">
        <DialogTitle>Route for {edit?.recordType}</DialogTitle>
        <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          <Err e={error} />
          <TextField label="Name" value={edit?.name ?? ''} onChange={(e) => setEdit({ ...edit!, name: e.target.value })} />
          <FormControlLabel control={<Switch checked={!!edit?.preventSelfApproval} onChange={(e) => setEdit({ ...edit!, preventSelfApproval: e.target.checked })} />} label="Submitter cannot approve" />
          {edit?.steps.map((s, i) => (
            <Stack key={i} direction="row" gap={1} alignItems="center">
              <Typography sx={{ width: 24, fontFamily: mono }}>{i + 1}</Typography>
              <TextField size="small" label="Step name" value={s.name} onChange={(e) => setStep(i, { name: e.target.value })} />
              <TextField size="small" select label="Required permission" value={s.requiredPermission} onChange={(e) => setStep(i, { requiredPermission: e.target.value })} sx={{ width: 240 }}>
                {(catalog.data ?? []).map((p) => <MenuItem key={p} value={p}>{p}</MenuItem>)}
              </TextField>
              <TextField size="small" label="Signature meaning" value={s.signatureMeaning} onChange={(e) => setStep(i, { signatureMeaning: e.target.value })} />
              <Button size="small" color="error" disabled={edit.steps.length === 1} onClick={() => setEdit({ ...edit, steps: edit.steps.filter((_, j) => j !== i) })}>Remove</Button>
            </Stack>
          ))}
          <Box><Button onClick={() => setEdit({ ...edit!, steps: [...edit!.steps, { order: 0, name: 'Review', requiredPermission: edit!.steps[0]?.requiredPermission ?? '', signatureMeaning: 'Reviewed' }] })}>Add step</Button></Box>
        </DialogContent>
        <DialogActions><Button onClick={() => setEdit(null)}>Cancel</Button>
          <Button variant="contained" onClick={async () => {
            setError(null);
            try { await put(`/api/admin/config/approval-routes/${edit!.recordType}`, { name: edit!.name, preventSelfApproval: edit!.preventSelfApproval, steps: edit!.steps.map((s, i) => ({ ...s, order: i + 1 })) }); setEdit(null); qc.invalidateQueries({ queryKey: ['routes'] }); }
            catch (e) { setError((e as Error).message); }
          }}>Save new version</Button></DialogActions>
      </Dialog>
    </ModuleWorkspace>
  );
}

interface Settings { applicationName: string; organizationName: string; primaryColor: string | null; timeZone: string; loginHistoryEnabled: boolean; rowVersion: number }
export function OrganizationPage() {
  const qc = useQueryClient();
  const s = useQuery({ queryKey: ['settings'], queryFn: () => get<Settings>('/api/organization/settings') });
  const [v, setV] = useState<Settings | null>(null);
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const cur = v ?? s.data;
  if (!cur) return null;
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Settings & branding' }]} title="Settings & branding">
      <Paper sx={{ p: 3, maxWidth: 640, display: 'grid', gap: 2 }}>
        {msg && <Alert severity={msg.ok ? 'success' : 'error'}>{msg.text}</Alert>}
        <TextField label="Application name" value={cur.applicationName} onChange={(e) => setV({ ...cur, applicationName: e.target.value })} />
        <TextField label="Organization name" value={cur.organizationName} onChange={(e) => setV({ ...cur, organizationName: e.target.value })} />
        <TextField label="Primary color (#RRGGBB, blank for default)" value={cur.primaryColor ?? ''} onChange={(e) => setV({ ...cur, primaryColor: e.target.value || null })} />
        <TextField label="Time zone (IANA)" value={cur.timeZone} onChange={(e) => setV({ ...cur, timeZone: e.target.value })} />
        <FormControlLabel control={<Switch checked={cur.loginHistoryEnabled} onChange={(e) => setV({ ...cur, loginHistoryEnabled: e.target.checked })} />} label="Show login history to administrators" />
        <Box><Button variant="contained" onClick={async () => {
          try { await put('/api/organization/settings', cur); setV(null); setMsg({ ok: true, text: 'Saved.' }); qc.invalidateQueries({ queryKey: ['settings'] }); qc.invalidateQueries({ queryKey: ['branding'] }); }
          catch (e) { setMsg({ ok: false, text: (e as Error).message }); }
        }}>Save</Button></Box>
      </Paper>
    </ModuleWorkspace>
  );
}

interface AuditRow { id: number; occurredAt: string; userName: string; action: string; entityType: string; description: string; reason: string | null }
export function AuditPage() {
  const [page, setPage] = useState(1);
  const [type, setType] = useState('');
  const a = useQuery({ queryKey: ['audit', page, type], queryFn: () => get<{ items: AuditRow[]; total: number; pageSize: number }>(`/api/audit-trail?page=${page}&pageSize=50${type ? `&entityType=${type}` : ''}`) });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Audit trail' }]} title="Audit trail">
      <Typography color="text.secondary" sx={{ mb: 2 }}>Every change to a quality record: who, what, when and a description. Entries cannot be edited or deleted.</Typography>
      <TextField size="small" label="Record type" value={type} onChange={(e) => { setType(e.target.value); setPage(1); }} sx={{ mb: 2, width: 260 }} placeholder="e.g. ControlledDocument" />
      <DataTable data={a.data?.items ?? []} columns={[
        { header: 'When', accessorKey: 'occurredAt', cell: (c) => fmtDateTime(c.getValue()) },
        { header: 'Who', accessorKey: 'userName' },
        { header: 'What', accessorKey: 'action' },
        { header: 'Record type', accessorKey: 'entityType' },
        { header: 'Description', accessorKey: 'description' },
      ]} />
      {a.data && <Pagination sx={{ mt: 2 }} count={Math.max(1, Math.ceil(a.data.total / a.data.pageSize))} page={page} onChange={(_, v) => setPage(v)} />}
    </ModuleWorkspace>
  );
}

interface SecRow { id: number; occurredAt: string; eventType: string; userName: string | null; ipAddress: string | null; succeeded: boolean; detail: string | null }
export function SecurityPage() {
  const [page, setPage] = useState(1);
  const a = useQuery({ queryKey: ['security', page], queryFn: () => get<{ items: SecRow[]; total: number; pageSize: number }>(`/api/security-events?page=${page}&pageSize=50`) });
  return (
    <ModuleWorkspace module={mod} crumbs={[{ label: 'Security events' }]} title="Security events">
      <DataTable data={a.data?.items ?? []} columns={[
        { header: 'When', accessorKey: 'occurredAt', cell: (c) => fmtDateTime(c.getValue()) },
        { header: 'Event', accessorKey: 'eventType' },
        { header: 'User', accessorKey: 'userName' },
        { header: 'IP address', accessorKey: 'ipAddress' },
        { header: 'Result', accessorKey: 'succeeded', cell: (c) => <Chip size="small" color={c.getValue() ? 'success' : 'error'} label={c.getValue() ? 'Success' : 'Failure'} /> },
        { header: 'Detail', accessorKey: 'detail' },
      ]} />
      {a.data && <Pagination sx={{ mt: 2 }} count={Math.max(1, Math.ceil(a.data.total / a.data.pageSize))} page={page} onChange={(_, v) => setPage(v)} />}
    </ModuleWorkspace>
  );
}

const zones: string[] = (Intl as unknown as { supportedValuesOf?: (k: string) => string[] }).supportedValuesOf?.('timeZone') ?? ['America/New_York', 'America/Chicago', 'America/Denver', 'America/Los_Angeles', 'UTC'];

/** Site time zone: business dates (today, due, overdue, effective) at the site follow it. */
function TimeZoneSelect({ value, onChange }: { value: string; onChange: (tz: string) => void }) {
  return (
    <TextField select size="small" label="Time zone" value={value} onChange={(e) => onChange(e.target.value)} sx={{ width: 220 }}>
      {(zones.includes(value) ? zones : [value, ...zones]).map((z) => <MenuItem key={z} value={z}>{z}</MenuItem>)}
    </TextField>
  );
}
