export type LeadStatus = "Open" | "Won" | "Lost" | "Archived";
export type StageKind = "Open" | "Won" | "Lost";
export type PurchaseTimeframe = "Unknown" | "Immediate" | "UpTo3Months" | "UpTo6Months" | "UpTo12Months" | "Over12Months";

export type Me = {
  userId: string;
  email: string;
  displayName: string;
  organizationId: string;
  organizationName: string;
  teamId: string | null;
  role: string;
  /** Somente para UX. A autorização real é feita pela API. */
  capabilities: Record<string, "own" | "team" | "org">;
  isGuest: boolean;
  accessExpiresAt: string | null;
};

export type LeadCard = {
  id: string;
  contactName: string;
  phone: string | null;
  ownerUserId: string;
  ownerName: string;
  stageId: string;
  status: LeadStatus;
  source: string | null;
  interest: string | null;
  score: number | null;
  tags: string[];
  enteredAt: string;
  stageEnteredAt: string;
  lastContactAt: string | null;
  nextFollowUpAt: string | null;
  version: number;
};

export type BoardStage = {
  id: string;
  name: string;
  kind: StageKind;
  color: string | null;
  position: number;
  total: number;
  leads: LeadCard[];
};

export type Board = { pipelineId: string; pipelineName: string; stages: BoardStage[] };

export type LeadDetail = {
  id: string;
  contactId: string;
  fullName: string;
  phone: string | null;
  email: string | null;
  ownerUserId: string;
  ownerName: string;
  pipelineId: string;
  stageId: string;
  stageName: string;
  status: LeadStatus;
  source: string | null;
  campaign: string | null;
  interest: string | null;
  developmentId: string | null;
  lotId: string | null;
  budgetMinCents: number | null;
  budgetMaxCents: number | null;
  timeframe: PurchaseTimeframe;
  region: string | null;
  notes: string | null;
  tags: string[];
  score: number | null;
  lostReason: string | null;
  enteredAt: string;
  stageEnteredAt: string;
  lastContactAt: string | null;
  nextFollowUpAt: string | null;
  version: number;
};

export type Paged<T> = { items: T[]; page: number; pageSize: number; total: number };

export type HistoryItem = {
  occurredAt: string;
  action: string;
  actorKind: "User" | "Copilot" | "System";
  actorId: string | null;
  actorName: string | null;
  data: string;
};

export type UserSummary = { userId: string; displayName: string; teamId: string | null; role: string };

export type Stage = { id: string; name: string; kind: StageKind; color: string | null; position: number; isArchived: boolean };
export type Pipeline = { id: string; name: string; isDefault: boolean; stages: Stage[] };

// ───────────── Fase 2: calendário, agenda, tarefas, macros ─────────────

export type WeeklyInterval = { isoWeekday: number; start: string; end: string };
export type CoverageRules = { offHours: boolean; saturdays: boolean; sundays: boolean; holidays: boolean };

export type CalendarSettings = {
  timeZone: string;
  stateCode: string | null;
  cityIbgeCode: string | null;
  businessHours: WeeklyInterval[];
  coverage: CoverageRules;
};

export type CalendarStatus = {
  at: string;
  isBusinessTime: boolean;
  dayClassification: string;
  coverageMode: "BusinessHours" | "OffHours" | "Weekend" | "Holiday" | "Closed";
  copilotMayCover: boolean;
  reason: string;
  nextBusinessStart: string | null;
};

export type Holiday = {
  id: string;
  date: string;
  name: string;
  scope: "National" | "State" | "City";
  stateCode: string | null;
  cityIbgeCode: string | null;
  source: "Manual" | "Api";
  isConfirmed: boolean;
  appliesToOrganization: boolean;
};

export type CalendarExceptionItem = { id: string; date: string; kind: "Closed" | "SpecialHours"; start: string | null; end: string | null; note: string | null };

export type AgentSchedule = {
  userId: string;
  isConfigured: boolean;
  workBlocks: WeeklyInterval[];
  visitDurationMinutes: number;
  bufferMinutes: number;
  minNoticeMinutes: number;
  maxVisitsPerDay: number | null;
  worksOnHolidays: boolean;
};

export type AppointmentKind = "Visit" | "Meeting" | "Call" | "Personal";
export type AppointmentStatus = "Scheduled" | "Completed" | "Cancelled" | "NoShow";

export type Appointment = {
  id: string;
  agentUserId: string;
  agentName: string;
  leadId: string | null;
  leadName: string | null;
  kind: AppointmentKind;
  title: string;
  startsAt: string;
  endsAt: string;
  location: string | null;
  notes: string | null;
  status: AppointmentStatus;
  cancelReason: string | null;
  createdByKind: string;
  version: number;
};

export type AbsenceKind = "Vacation" | "DayOff" | "Blocked" | "Sick" | "Other";
export type Absence = { id: string; userId: string; startsAt: string; endsAt: string; kind: AbsenceKind; note: string | null };
export type Range = { start: string; end: string };
export type AgendaDay = { date: string; classification: string; holidayName: string | null; workWindows: Range[] };

export type Agenda = {
  userId: string;
  timeZone: string;
  schedule: AgentSchedule;
  appointments: Appointment[];
  absences: Absence[];
  days: AgendaDay[];
};

export type Availability = { userId: string; isScheduleConfigured: boolean; durationMinutes: number; slots: Range[] };

export type TaskKind = "FollowUp" | "Call" | "Message" | "Document" | "Other";
export type TaskStatus = "Open" | "Done" | "Cancelled";

export type TaskItem = {
  id: string;
  assigneeUserId: string;
  assigneeName: string;
  leadId: string | null;
  leadName: string | null;
  kind: TaskKind;
  title: string;
  notes: string | null;
  dueAt: string;
  status: TaskStatus;
  completedAt: string | null;
  createdByKind: string;
};

export type Macro = {
  id: string;
  shortcut: string;
  title: string;
  body: string;
  isShared: boolean;
  ownerUserId: string | null;
  developmentId: string | null;
  isActive: boolean;
  usageCount: number;
  canEdit: boolean;
};

export type MacroVariable = { name: string; description: string };
export type RenderResult = { text: string; missingVariables: string[] };

// ───────────── Acessos externos, sugestões, desenvolvedor ─────────────

export type GuestRole = "Visitante" | "Desenvolvedor";

export type AccessLink = {
  id: string;
  label: string;
  role: GuestRole;
  note: string | null;
  createdAt: string;
  expiresAt: string;
  revokedAt: string | null;
  lastUsedAt: string | null;
  useCount: number;
  createdByName: string;
  status: "Active" | "Expired" | "Revoked";
};

export type CreatedAccessLink = { id: string; token: string; expiresAt: string };

export type SuggestionKind = "Bug" | "Improvement" | "Idea" | "Text";
export type SuggestionStatus = "New" | "UnderReview" | "Planned" | "Done" | "Declined";
export type SuggestionPriority = "Low" | "Medium" | "High";

export type SuggestionSummary = {
  id: string;
  kind: SuggestionKind;
  title: string;
  pagePath: string;
  status: SuggestionStatus;
  priority: SuggestionPriority | null;
  authorUserId: string;
  authorName: string;
  authorRole: string;
  commentCount: number;
  createdAt: string;
  updatedAt: string;
};

export type SuggestionComment = { id: string; authorUserId: string; authorName: string; body: string; createdAt: string };

export type SuggestionDetail = Omit<SuggestionSummary, "commentCount" | "updatedAt"> & {
  description: string;
  pageTitle: string | null;
  elementDescriptor: string | null;
  viewport: string | null;
  comments: SuggestionComment[];
  canTriage: boolean;
  canComment: boolean;
};

export type SuggestionStats = { new: number; underReview: number; planned: number; done: number; declined: number };

export type SystemInfo = {
  environment: string;
  appVersion: string;
  runtime: string;
  serverTimeUtc: string;
  organizationTimeZone: string;
  databaseVersion: string;
  appliedMigrations: string[];
  pendingMigrations: string[];
};

export type OutboxStats = {
  pending: number;
  retrying: number;
  deadLettered: number;
  processedLast24h: number;
  oldestPendingSeconds: number | null;
  recentFailures: { id: string; type: string; attempts: number; lastError: string | null; occurredAt: string; availableAt: string; deadLettered: boolean }[];
};

export type AuditEntry = {
  sequence: number;
  occurredAt: string;
  actorKind: string;
  actorId: string | null;
  actorName: string | null;
  action: string;
  entityType: string;
  entityId: string | null;
  data: string;
  correlationId: string | null;
};

export type EndpointInfo = { method: string; route: string; group: string; anonymous: boolean };
export type RoleMatrix = { capabilities: string[]; roles: { role: string; isSystem: boolean; grants: Record<string, string> }[] };
