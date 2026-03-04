# ReRhythm - Complete Application Flow & Architecture

## TABLE OF CONTENTS
1. Application Overview
2. User Journey Flow
3. Feature Breakdown by Tier
4. Database Architecture
5. AWS Services Integration
6. Algorithms & Business Logic
7. Security & Compliance

---

## 1. APPLICATION OVERVIEW

### Tech Stack
- Backend: ASP.NET Core 8.0 (C#)
- Frontend: Razor Views + Tailwind CSS
- Cloud: AWS (Bedrock, DynamoDB, S3, Textract, Rekognition, Comprehend, SES)
- Deployment: ECS Fargate (containerized)
- Infrastructure: CloudFormation (IaC)

### Core Purpose
AI-powered 28-day career coaching platform that analyzes resumes and generates personalized learning roadmaps to help users land their target role.

---

## 2. USER JOURNEY FLOW

### Phase 1: Onboarding (Resume Upload)
```
User lands on homepage
  |
  v
Upload Resume (PDF/DOCX/TXT)
  |
  v
AWS Textract extracts text + structure
  |
  v
Parse resume data (skills, experience, education, projects)
  |
  v
Enter target role + industry
  |
  v
Optional: RIASEC personality test (6 questions)
  |
  v
Optional: Custom skills to learn
  |
  v
Generate 28-day roadmap (AWS Bedrock Claude Sonnet 4.5)
```

### Phase 2: Learning Journey (28 Days)
```
View personalized roadmap
  |
  v
4 Weekly Modules (Week 1-4)
  |
  v
Each week: 7 Daily Sprints (15-min lessons)
  |
  v
Lesson formats:
  - Flashcards (interactive learning)
  - Quizzes (knowledge testing)
  - Labs (hands-on practice)
  |
  v
Complete lessons -> Track progress
  |
  v
Earn badges (First Lesson, Week Complete, Streak, etc.)
  |
  v
Week 4 completion -> Generate certificate
```

### Phase 3: Post-Completion
```
Complete 28 days
  |
  v
Download completion certificate (PDF)
  |
  v
Generate AI-powered future-ready resume
  |
  v
Access advanced features:
  - Analytics dashboard
  - Community features
  - Job simulator
  - Premium upgrades
```

### Phase 4: Community Engagement
```
Access Community section
  |
  v
Forum: Ask questions, get answers, vote
  |
  v
Networking: Connect with peers in same industry
  |
  v
Project Showcase: Share portfolio, get peer reviews
  |
  v
Build reputation through quality contributions
```

---

## 3. FEATURE BREAKDOWN BY TIER

### BASIC TIER (Free)
**Core Learning**
- Resume upload & parsing (AWS Textract)(personalization test)
- AI roadmap generation (28-day plan)
- 28 daily lessons (Videos and Links, quizzes, labs)
- Progress tracking
- Badge system (7 badges)
- Completion certificate
- AI-generated resume

**Community**
- Forum Q&A (post questions, answer, vote)
- Networking (connect with peers)
- Project Showcase (share portfolio, get reviews)
- Reputation system

**Analytics**
- Basic progress dashboard
- Resume quality score
- Skill gap analysis

### SILVER TIER ($9.99/month)
All Basic features PLUS:
- Advanced analytics dashboard
- Industry insights
- Salary benchmarking
- Career path recommendations
- Priority support

### GOLD TIER ($19.99/month)
All Silver features PLUS:
- 1-on-1 mentoring (coming soon)
- Live Q&A sessions (coming soon)
- Exclusive workshops
- Job placement assistance
- Resume review by experts

### PREMIUM FEATURES (Planned)
- Mock interview simulator
- Advanced job matching
- Personalized career coaching
- Certification exam prep

---

## 4. DATABASE ARCHITECTURE

### DynamoDB Tables (9 Total)

**1. rerythm-roadmaps**
```
Purpose: Store user roadmaps and profile data
Primary Key: UserId (HASH), createdAt (RANGE)
Attributes:
  - UserId, FullName, Email, PhoneNumber
  - TargetRole, Industry
  - TotalYearsOfExperience, YearsInTargetIndustry
  - PersonalityType, SubscriptionTier
  - Modules (4 weeks x 7 days)
  - SkillsIdentified, SkillsToAcquire
  - OriginalResumeText, ParsedResumeData
  - GeneratedAt
Encryption: KMS
Point-in-time recovery: Enabled
```

**2. rerythm-lesson-plans**
```
Purpose: Track individual lesson completion
Primary Key: UserId (HASH), moduleId (RANGE)
Attributes:
  - UserId, ModuleId
  - WeekNumber, DayNumber
  - Topic, TargetRole
  - IsCompleted, CompletedAt
  - Flashcards, QuizQuestions
  - LabInstructions, LabSolution
  - CreatedAt
GSI: None
```

**3. rerythm-badge-achievements**
```
Purpose: Track user badges and achievements
Primary Key: UserId (HASH), badgeId (RANGE)
Attributes:
  - UserId, BadgeId
  - BadgeName, Description
  - EarnedAt
GSI: None
```

**4. rerythm-forum-questions**
```
Purpose: Community forum questions
Primary Key: QuestionId (HASH)
Attributes:
  - QuestionId, UserId, UserName
  - Industry, Title, Content
  - ImageUrls (list)
  - AcceptedAnswerId
  - ViewCount, CreatedAt
GSI: Industry-CreatedAt-Index
  - Enables browsing questions by industry
```

**5. rerythm-forum-answers**
```
Purpose: Forum answers and responses
Primary Key: AnswerId (HASH)
Attributes:
  - AnswerId, QuestionId
  - UserId, UserName, Content
  - Upvotes, Downvotes
  - UpvotedBy, DownvotedBy (lists)
  - CreatedAt
GSI: QuestionId-CreatedAt-Index
  - Enables fetching all answers for a question
```

**6. rerythm-forum-notifications**
```
Purpose: User notifications for forum activity
Primary Key: NotificationId (HASH)
Attributes:
  - NotificationId, UserId
  - Type (answer, vote, accepted, violation)
  - Message, QuestionId, AnswerId
  - IsRead, CreatedAt
GSI: UserId-CreatedAt-Index
  - Enables fetching user notifications
```

**7. rerythm-connections**
```
Purpose: Professional networking connections
Primary Key: ConnectionId (HASH)
Attributes:
  - ConnectionId
  - FromUserId, ToUserId
  - Status (Pending, Accepted, Rejected)
  - Message, CreatedAt
GSI: FromUserId-Status-Index
  - Query sent connection requests
GSI: ToUserId-Status-Index
  - Query received connection requests
```

**8. rerythm-projects**
```
Purpose: Portfolio project showcase
Primary Key: ProjectId (HASH)
Attributes:
  - ProjectId, UserId, UserName
  - Title, Description, TechStack
  - GithubUrl, LiveUrl
  - ImageUrls (list, max 3)
  - Industry
  - ViewCount, ReviewCount, AverageRating
  - CreatedAt
GSI: Industry-CreatedAt-Index
  - Browse projects by industry
GSI: UserId-CreatedAt-Index
  - Get user's projects for reputation calculation
```

**9. rerythm-project-reviews**
```
Purpose: Peer reviews for projects
Primary Key: ReviewId (HASH)
Attributes:
  - ReviewId, ProjectId
  - ReviewerId, ReviewerName
  - Rating (1-5), Comment
  - CreatedAt
GSI: ProjectId-CreatedAt-Index
  - Fetch all reviews for a project
```

### S3 Buckets (3 Total)

**1. rerythm-resume-uploads-{AccountId}**
```
Purpose: Store uploaded resumes
Encryption: KMS (S3KMSKey)
Versioning: Enabled
Public Access: Blocked
Lifecycle: Not configured (manual cleanup)
```

**2. rerythm-forum-images-{AccountId}**
```
Purpose: Store forum question images
Encryption: AES256
Versioning: Enabled
Public Access: Allowed (read-only via bucket policy)
CORS: Enabled for GET requests
Max images per question: 2
```

**3. rerythm-project-images-{AccountId}**
```
Purpose: Store project showcase screenshots
Encryption: AES256
Versioning: Enabled
Public Access: Allowed (read-only via bucket policy)
CORS: Enabled for GET requests
Max images per project: 3
```

---

## 5. AWS SERVICES INTEGRATION

### AWS Bedrock (Claude Sonnet 4.5)
**Purpose**: AI-powered content generation
**Model**: us.anthropic.claude-sonnet-4-20250514-v1:0
**Use Cases**:
1. Generate 28-day roadmap from resume
2. Generate flashcards for lessons
3. Generate quiz questions
4. Generate lab exercises
5. Generate future-ready resume
6. Generate advanced learning topics

**Integration Flow**:
```
User input -> BedrockRAGService
  |
  v
Construct prompt with context
  |
  v
Call InvokeModelAsync (Bedrock Runtime)
  |
  v
Parse JSON response
  |
  v
Return structured data
```

**Configuration**:
- Max tokens: 4096
- Temperature: 0.7 (balanced creativity)
- Top P: 0.9
- VPC Endpoint: bedrock-runtime (private access)

### AWS Textract
**Purpose**: Resume parsing and text extraction
**API**: AnalyzeDocument
**Features Used**:
- FORMS: Extract key-value pairs
- TABLES: Extract structured data

**Integration Flow**:
```
User uploads resume -> S3
  |
  v
TextractService.AnalyzeResumeAsync()
  |
  v
Call AnalyzeDocument with S3 object
  |
  v
Parse blocks (KEY_VALUE_SET, LINE, WORD)
  |
  v
Extract: Name, Email, Phone, Skills, Experience, Education
  |
  v
Return ParsedResumeData
```

### AWS Rekognition
**Purpose**: Image moderation for forum/project images
**API**: DetectModerationLabels
**Threshold**: 60% confidence

**Integration Flow**:
```
User uploads image -> ForumService/ProjectService
  |
  v
Upload to S3 first
  |
  v
Call DetectModerationLabels
  |
  v
Check for inappropriate content
  |
  v
If flagged: Delete from S3 + notify user
  |
  v
If clean: Allow display
```

**Moderation Categories**:
- Explicit content
- Violence
- Hate symbols
- Drugs
- Weapons

### AWS Comprehend
**Purpose**: Text toxicity detection for forum posts
**API**: DetectToxicContent
**Threshold**: 70% confidence

**Integration Flow**:
```
User posts content -> ForumService
  |
  v
Quick fallback check (profanity filter)
  |
  v
Call DetectToxicContent
  |
  v
Check toxicity labels (profanity, hate speech, harassment)
  |
  v
If toxic: Delete post + notify user
  |
  v
If clean: Allow post
```

### AWS SES (Simple Email Service)
**Purpose**: Send email notifications
**Sender**: mamootilsamuel1@gmail.com (verified)

**Email Types**:
1. Forum violation notifications
2. New question notifications (industry-based)
3. Connection request notifications (future)

### AWS DynamoDB
**Purpose**: NoSQL database for all application data
**Billing**: Pay-per-request (on-demand)
**Features Used**:
- Global Secondary Indexes (GSI)
- Point-in-time recovery
- KMS encryption
- VPC Gateway Endpoint (private access)

### AWS KMS (Key Management Service)
**Purpose**: Encryption key management
**Keys Created**:
1. S3KMSKey - Encrypts resume uploads bucket
2. DynamoDBKMSKey - Encrypts all DynamoDB tables

---

## 6. ALGORITHMS & BUSINESS LOGIC

### Algorithm 1: Resume Quality Score
**Location**: AnalyticsService.CalculateResumeScoreAsync()
**Formula**:
```
Base Score = 0

Skills Section:
  + 20 points if skills present
  + 5 points per skill (max 30 points)

Experience Section:
  + 25 points if experience present
  + 5 points per year (max 25 points)

Education Section:
  + 15 points if education present
  + 10 points if degree mentioned

Projects Section:
  + 15 points if projects present

Contact Info:
  + 5 points if email present
  + 5 points if phone present

Total: 0-100 points
```

**Output**:
- Score: 0-100
- Grade: A (90+), B (80-89), C (70-79), D (60-69), F (<60)
- Recommendations: List of improvements

### Algorithm 2: Skill Gap Analysis
**Location**: AnalyticsService.AnalyzeSkillGapsAsync()
**Logic**:
```
1. Extract current skills from resume
2. Query DynamoDB for all users in same industry
3. Aggregate skill frequencies
4. Identify top 10 most common skills
5. Compare user skills vs industry skills
6. Return missing skills (gaps)
```

**Output**:
- Current skills: List
- Missing skills: List with frequency counts
- Skill coverage: Percentage

### Algorithm 3: Reputation Score
**Location**: ProjectService.GetUserReputationAsync()
**Formula**:
```
Reputation = Σ (ReviewCount × AverageRating × 10)

For each project:
  Project Score = ReviewCount × AverageRating × 10

Total Reputation = Sum of all project scores
```

**Example**:
```
User has 3 projects:
- Project A: 5 reviews, 4.5 avg = 5 × 4.5 × 10 = 225 points
- Project B: 3 reviews, 5.0 avg = 3 × 5.0 × 10 = 150 points
- Project C: 8 reviews, 4.0 avg = 8 × 4.0 × 10 = 320 points

Total Reputation = 695 points
```

### Algorithm 4: Badge Awarding
**Location**: BadgeService.CheckAndAwardBadgesAsync()
**Badges**:
```
1. First Lesson (complete 1 lesson)
2. Week Warrior (complete 7 lessons in a week)
3. Halfway Hero (complete 14 lessons)
4. Sprint Master (complete 28 lessons)
5. Streak Keeper (7-day streak)
6. Early Bird (complete lesson before 9 AM)
7. Night Owl (complete lesson after 9 PM)
```

**Logic**:
```
For each badge:
  1. Check if user meets criteria
  2. Check if badge already awarded
  3. If eligible and not awarded:
     - Create badge record in DynamoDB
     - Return badge details
```

### Algorithm 5: Content Moderation
**Location**: ForumService.ModerateTextAsync() + ModerateImageAsync()

**Text Moderation**:
```
Step 1: Fallback Filter (fast)
  - Check profanity list
  - Check excessive punctuation (>5 ! or ?)
  - Check excessive links (>3 URLs)
  - Check excessive caps (>70% uppercase)

Step 2: AWS Comprehend (deep)
  - Call DetectToxicContent
  - Check toxicity score (threshold: 0.7)
  - Categories: profanity, hate speech, harassment, insult

If either fails: Delete content + notify user
```

**Image Moderation**:
```
Step 1: Upload to S3
Step 2: Call Rekognition DetectModerationLabels
Step 3: Check confidence (threshold: 60%)
Step 4: If flagged: Delete from S3 + notify user
```

### Algorithm 6: Roadmap Generation
**Location**: RoadmapService.GenerateRoadmapAsync()
**Process**:
```
1. Construct AI prompt with:
   - Resume text
   - Target role
   - Industry
   - Years of experience
   - Personality type (optional)
   - Custom skills (optional)

2. Call Bedrock Claude Sonnet 4.5

3. Parse JSON response:
   {
     "modules": [
       {
         "weekNumber": 1,
         "theme": "Foundations",
         "dailySprints": [
           {
             "day": 1,
             "topic": "Introduction to X",
             "lessonFormat": "flashcard",
             "estimatedMinutes": 15
           }
         ],
         "portfolioProject": "Build X"
       }
     ],
     "skillsIdentified": [...],
     "skillsToAcquire": [...]
   }

4. Save to DynamoDB (rerythm-roadmaps)

5. Create 28 lesson records (rerythm-lesson-plans)

6. Return RoadmapPlan object
```

---

## 7. SECURITY & COMPLIANCE

### Data Encryption
**At Rest**:
- DynamoDB: KMS encryption (DynamoDBKMSKey)
- S3 Resumes: KMS encryption (S3KMSKey)
- S3 Images: AES256 encryption

**In Transit**:
- HTTPS/TLS 1.3 (ALB with ACM certificate)
- VPC Endpoints for AWS services (private traffic)

### Access Control
**IAM Roles**:
- ECSTaskExecutionRole: Pull images, write logs
- ECSTaskRole: Access DynamoDB, S3, Bedrock, Textract, etc.

**Permissions**:
- Least privilege principle
- No wildcard (*) permissions
- Resource-specific ARNs

### Network Security
**VPC Architecture**:
- Public subnets: ALB only
- Private subnets: ECS tasks (no internet access)
- VPC Endpoints: Private access to AWS services

**Security Groups**:
- ALB SG: Allow 80/443 from internet
- ECS Task SG: Allow 8080 from ALB only
- VPC Endpoint SG: Allow 443 from VPC CIDR

### PII Protection
**Resume Data**:
- Stored encrypted in S3
- Parsed data in DynamoDB (encrypted)
- No PII in logs
- Email/phone extracted but encrypted

**User Data**:
- No passwords (stateless auth via userId)
- Email used only for notifications
- Phone number optional

### Content Moderation
**Automated**:
- AWS Rekognition for images
- AWS Comprehend for text
- Fallback profanity filter

**Manual**:
- Admin review for flagged content (future)
- User reporting system (future)

---

## 8. DEPLOYMENT ARCHITECTURE

### Infrastructure
```
CloudFormation Stack
  |
  +-- VPC (10.0.0.0/16)
  |     |
  |     +-- Public Subnets (ALB)
  |     +-- Private Subnets (ECS Tasks)
  |     +-- VPC Endpoints (Bedrock, Textract, DynamoDB, S3, etc.)
  |
  +-- Application Load Balancer
  |     |
  |     +-- HTTPS Listener (443) -> Target Group
  |     +-- HTTP Listener (80) -> Redirect to HTTPS
  |
  +-- ECS Fargate Cluster
  |     |
  |     +-- Service (1 task)
  |           |
  |           +-- Task Definition
  |                 |
  |                 +-- Container (rerythm:latest)
  |                       - CPU: 1024 (1 vCPU)
  |                       - Memory: 2048 MB
  |                       - Port: 8080
  |
  +-- DynamoDB Tables (9)
  +-- S3 Buckets (3)
  +-- KMS Keys (2)
  +-- CloudWatch Logs
```

### Scaling
- ECS Service: Auto-scaling (future)
- DynamoDB: On-demand (auto-scales)
- S3: Unlimited storage
- ALB: Auto-scales

---

## 9. COST ESTIMATION

### Monthly Costs (Estimated)
```
ECS Fargate (1 task, 24/7):
  - 1 vCPU × 730 hours = $29.93
  - 2 GB RAM × 730 hours = $6.57
  Subtotal: $36.50

Application Load Balancer:
  - Fixed: $16.20
  - LCU: ~$5/month (low traffic)
  Subtotal: $21.20

DynamoDB (on-demand):
  - 1M reads: $0.25
  - 1M writes: $1.25
  - Storage: 1 GB = $0.25
  Subtotal: ~$2/month

S3:
  - Storage: 10 GB = $0.23
  - Requests: Minimal
  Subtotal: ~$0.50/month

Bedrock (Claude Sonnet 4.5):
  - Input: $3 per 1M tokens
  - Output: $15 per 1M tokens
  - Estimated: 100 roadmaps/month = ~$10
  Subtotal: ~$10/month

Other Services (Textract, Rekognition, Comprehend, SES):
  - Estimated: ~$5/month

Total: ~$75-80/month (low traffic)
```

---

## 10. FEATURE SUMMARY

### Total Features Implemented: 25+

**Core Features (8)**:
1. Resume upload & parsing
2. AI roadmap generation
3. 28-day learning journey
4. Flashcards, quizzes, labs
5. Progress tracking
6. Badge system
7. Certificate generation
8. AI resume builder

**Community Features (3)**:
9. Forum Q&A
10. Professional networking
11. Project showcase

**Analytics Features (3)**:
12. Progress dashboard
13. Resume quality score
14. Skill gap analysis

**Premium Features (2)**:
15. Advanced analytics
16. Industry insights

**Planned Features (9)**:
17. 1-on-1 mentoring
18. Live Q&A sessions
19. Mock interviews
20. Job matching
21. Salary negotiation
22. Career coaching
23. Certification prep
24. Resume review
25. Job placement

---

## 11. KEY METRICS TO TRACK

### User Engagement
- Daily active users
- Lesson completion rate
- Average time per lesson
- 28-day completion rate
- Badge earn rate

### Community Engagement
- Forum questions posted
- Forum answers posted
- Connection requests sent
- Projects shared
- Reviews given

### Business Metrics
- Free to paid conversion rate
- Monthly recurring revenue
- Churn rate
- Customer lifetime value
- Net promoter score

---

END OF DOCUMENTATION
