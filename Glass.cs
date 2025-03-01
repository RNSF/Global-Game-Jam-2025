using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public partial class Glass : RigidBody2D
{
	[Export]
	public SodaFluid sodaFluid;
	public bool isPickedUp = false;

	private Area2D OuterArea {
		get => GetNode<Area2D>("OuterArea");
	}

	private Area2D InnerArea {
		get => GetNode<Area2D>("InnerArea");
	}

	private CollisionPolygon2D InnerAreaShape {
		get => GetNode<CollisionPolygon2D>("InnerArea/CollisionPolygon2D");
	}

	private SodaSurface SodaSurfaceNode {
		get => GetNode<SodaSurface>("SodaSurface");
	}
	
	private bool isOnBar = false;
	private bool isBehindBar = false;

	ConvexPolygonShape2D innerShape;
	public float[] sodaComposition;
	public float sodaVolume;
	public float fizzLevel;
	int tick;
	private Vector2 previousGlobalPosition;

	public AudioStreamPlayer GlassCollisionSound {
		get => GetNode<AudioStreamPlayer>("GlassCollision");
	}

	public AudioStreamPlayer FillSound {
		get => GetNode<AudioStreamPlayer>("FillSound");
	}

    public override void _Ready()
    {
		Debug.Assert(sodaFluid != null);
        innerShape = new ConvexPolygonShape2D();
		innerShape.Points = InnerAreaShape.Polygon;

		this.BodyEntered += (body) => {
			
			if (body is PhysicsBody2D physicsBody) {
				var loudness = Mathf.Clamp(Mathf.Remap(LinearVelocity.Length(), 0.0f, 30.0f, 0.0f, 1.0f), 0.0f, 1.0f);
				if ((physicsBody.CollisionLayer & (CollisionLayers.BOTTLES | CollisionLayers.GLASS)) != 0) {
					
					GlassCollisionSound.VolumeDb = Mathf.LinearToDb(loudness);
					GlassCollisionSound.Play();
				}
				
			} 
		};
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _PhysicsProcess(double delta)
	{
		
		isBehindBar = InnerArea.GetOverlappingBodies().Any((Node2D node) => {
			if (node is PhysicsBody2D body) {
				return (body.CollisionMask & CollisionLayers.BAR) != 0;
			}
			return false;
		});

		if (isBehindBar || isPickedUp || (tick < 1)) {
			CollisionMask &= ~CollisionLayers.BAR;
		} else {
			CollisionMask |= CollisionLayers.BAR;
		}

		// detect fluid
		var results = SodaCheck();

		sodaComposition = new float[Enum.GetNames(typeof(Soda.Type)).Length];
		var oldSodaVolume = sodaVolume;
		sodaVolume = 0.0f;
		fizzLevel = 0.0f;

		foreach (var result in results) {
			Rid rid = result["rid"].As<Rid>();
			if (!sodaFluid.bodyToParticle.ContainsKey(rid)) continue;
			SodaFluid.Particle particle = sodaFluid.bodyToParticle[rid];

			float volume = particle.radius * particle.radius;
			sodaVolume += volume;
			fizzLevel += particle.fizziness * volume;

			for (int i = 0; i < sodaComposition.Count(); i++) {
				sodaComposition[i] += particle.sodaComposition[i] * volume;
			}
		}

		if (sodaVolume > 0) {
			Color color = Colors.Black;

			fizzLevel /= sodaVolume;
			for (int i = 0; i < sodaComposition.Count(); i++) {
				sodaComposition[i] /= sodaVolume;
				color += sodaComposition[i] * Soda.GetColor((Soda.Type) i);
			}
			

			foreach (var result in results) {
				Rid rid = result["rid"].As<Rid>();
				SodaFluid.Particle particle = sodaFluid.bodyToParticle[rid];

				particle.fizziness = fizzLevel;
				for (int i = 0; i < sodaComposition.Count(); i++) {
					particle.sodaComposition[i] = sodaComposition[i];
				}

				RenderingServer.CanvasItemSetParent(particle.canvasItem, SodaSurfaceNode.CanvasGroupNode.GetCanvasItem());
				RenderingServer.CanvasItemSetModulate(particle.canvasItem, color);
			}
		}

		SodaSurfaceNode.fizziness = fizzLevel;


		previousGlobalPosition = GlobalPosition;
		tick ++;

		float volumePercent = Math.Clamp(sodaVolume / 8000, 0.0f, 1.0f);
		FillSound.VolumeDb = Mathf.LinearToDb(Mathf.Lerp(Mathf.DbToLinear(FillSound.VolumeDb), 0.0f, 8.0f * ((float)delta)));
		FillSound.PitchScale = Mathf.Lerp(0.4f, 2.0f, volumePercent);

		if (sodaVolume - oldSodaVolume > 10.0f) {
			FillSound.VolumeDb = Mathf.LinearToDb(Mathf.Lerp(1.0f, 0.0f, volumePercent));
			GD.Print(sodaVolume);
		}

	}

    public override void _IntegrateForces(PhysicsDirectBodyState2D state)
    {
		isOnBar = false;
		for (var i = 0; i < state.GetContactCount(); i++) {
			
			var obj = state.GetContactColliderObject(i);
			if (obj is PhysicsBody2D physicsBody2D) {
				if ((physicsBody2D.CollisionLayer & CollisionLayers.BAR) == 0) continue;
				isOnBar = isOnBar || state.GetContactLocalNormal(i).Dot(Vector2.Up) > 0.9;
			}
		}
        
    }

    public bool CanServe() {
		return !isBehindBar && isOnBar && Mathf.AngleDifference(GlobalRotation, 0) < 0.05;
	}

	public void DestroyFluid() {
		var results = SodaCheck();

		foreach (var result in results) {
			Rid rid = result["rid"].As<Rid>();
			sodaFluid.DestroyParticle(rid);
		}
	}

	public Godot.Collections.Array<Godot.Collections.Dictionary> SodaCheck() {
		var state = GetWorld2D().DirectSpaceState;
		var query = new PhysicsShapeQueryParameters2D();
		query.CollideWithAreas = false;
		query.CollideWithBodies = true;
		query.CollisionMask = CollisionLayers.FLUID;
		query.Shape = innerShape;
		query.Transform = GlobalTransform;
		return state.IntersectShape(query, 300);
	}
	
}
